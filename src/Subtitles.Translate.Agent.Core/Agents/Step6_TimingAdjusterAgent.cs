using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Subtitles.Translate.Agent.Core.Models;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Subtitles.Translate.Agent.Core.Agents
{
    /// <summary>
    /// Step 6: Timing Adjustment Agent
    /// Fine-tune subtitle end times based on reading length
    /// </summary>
    public class Step6_TimingAdjusterAgent : AgentBase
    {
        public const string AgentName = nameof(Step6_TimingAdjusterAgent);
        private readonly AIAgent _agent;
        private int _currentBatchIndex;

        /// <summary>
        /// Comfortable reading speed (chars/sec)
        /// </summary>
        private const double ComfortableCps = 5.0;

        /// <summary>
        /// CPS threshold, extension needed if exceeded
        /// </summary>
        private const double CpsThreshold = 7.0;

        /// <summary>
        /// Minimum frame gap (ms)
        /// </summary>
        private const double MinGapMs = 50;

        /// <summary>
        /// Default max extension (ms) - used for the last sentence
        /// </summary>
        private const double DefaultMaxExtensionMs = 1000;

        /// <summary>
        /// Minimum duration for short sentences (ms)
        /// </summary>
        private const double ShortLineMinDurationMs = 1000;

        public Step6_TimingAdjusterAgent(WorkflowContext context)
            : base(context, AgentName)
        {
            // Create ChatClient and initialize Agent
            var client = CreateChatClient(_agentConfig.ModelId);
            _agent = client.CreateAIAgent().AsBuilder().Build();
        }

        /// <summary>
        /// Execute timing adjustment (using AI)
        /// </summary>
        public override async Task ExecuteAsync()
        {
            _logger?.LogInformation("===== {AgentName} started execution (AI Mode) =====", AgentName);

            var paragraphs = _context.Subtitle.Paragraphs;
            var translatedItems = _context.TranslatedItems;
            int totalCount = Math.Min(paragraphs.Count, translatedItems.Count);

            // Initialize results
            _context.TimingAdjustments = new List<TimingAdjustmentItem>();
            _context.TimingStatistics = new TimingStatistics();

            // Use configured BatchSize, default 20
            int batchSize = _context.Request.BatchSize > 0 ? _context.Request.BatchSize : 20;
            _logger?.LogDebug("Subtitles to process: {Count}, Batch size: {BatchSize}", totalCount, batchSize);

            int startIndex = 0;
            int batchIndex = 0;

            while (startIndex < totalCount)
            {
                int count = Math.Min(batchSize, totalCount - startIndex);

                // Process batch with retry mechanism
                await ExecuteBatchWithRetryAsync(_context, startIndex, count, batchIndex);

                startIndex += count;
                batchIndex++;
            }

            _logger?.LogInformation("===== {AgentName} completed, extended {Extended}, kept {Kept} =====",
                AgentName, _context.TimingStatistics.ExtendedCount, _context.TimingStatistics.KeptCount);
        }


        private async Task ExecuteBatchWithRetryAsync(WorkflowContext context, int startIndex, int count, int batchIndex)
        {
            int maxRetries = context.MaxRetries;
            int retryCount = 0;

            while (retryCount <= maxRetries)
            {
                try
                {
                    await ProcessBatchAsync(context, startIndex, count, batchIndex);
                    return;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    if (retryCount <= maxRetries)
                    {
                        _logger?.LogWarning(ex, "Batch {BatchIndex} timing adjustment failed, retrying {RetryCount}/{MaxRetries}...",
                            batchIndex, retryCount, maxRetries);
                        await Task.Delay(1000 * retryCount);
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }

        private async Task ProcessBatchAsync(WorkflowContext context, int startIndex, int count, int batchIndex)
        {
            _currentBatchIndex = batchIndex;
            var prompt = GetPrompt(context, startIndex, count);

            _logger?.LogInformation("Calling LLM (Model: {ModelId}, Batch: {BatchIndex})...", _agentConfig.ModelId, batchIndex);
            var response = await _agent.RunAsync(prompt);

            // Record Token usage
            RecordTokenUsage(AgentName, response.Usage, batchIndex);

            // Parse response
            var adjustments = ParseJsonResponse<List<TimingAdjustmentItem>>(response.Text);

            if (adjustments != null && adjustments.Count > 0)
            {
                // Apply adjustments to original subtitles
                ApplyAdjustments(context, adjustments);
                context.TimingAdjustments.AddRange(adjustments);
            }
            else
            {
                _logger?.LogWarning("{AgentName} AI response parsing failed or empty (Batch {BatchIndex})", AgentName, batchIndex);
                // Throw exception to trigger retry
                throw new InvalidOperationException($"AI response parsing failed (Batch {batchIndex})");
            }
        }

        /// <summary>
        /// Generate AI timing adjustment prompt
        /// </summary>
        public string GetPrompt(WorkflowContext context, int startIndex, int count)
        {
            var paragraphs = context.Subtitle.Paragraphs;
            var translatedItems = context.TranslatedItems;

            // Build input data
            var inputList = new List<object>();

            int endIndex = startIndex + count;

            for (int i = startIndex; i < endIndex && i < translatedItems.Count && i < paragraphs.Count; i++)
            {
                var paragraph = paragraphs[i];
                var translation = translatedItems[i];

                var startTimeMs = paragraph.StartTime.TotalMilliseconds;
                var endTimeMs = paragraph.EndTime.TotalMilliseconds;

                double? nextLineStartMs = null;
                // Note: Need to get global next line, not just within batch
                if (i + 1 < paragraphs.Count)
                {
                    nextLineStartMs = paragraphs[i + 1].StartTime.TotalMilliseconds;
                }

                inputList.Add(new
                {
                    id = i.ToString(),
                    text = translation.Translation,
                    start_time = FormatTimeCode(startTimeMs),
                    end_time = FormatTimeCode(endTimeMs),
                    next_line_start_time = nextLineStartMs.HasValue ? FormatTimeCode(nextLineStartMs.Value) : null
                });
            }

            var inputData = JsonSerializer.Serialize(inputList, DefaultJsonOptions);

            var prompt = $$"""
# Role 
 You are a Subtitle Timing Specialist. 
 **Core Task**: Optimize subtitle `end_time` based on text length to ensure viewers have enough reading time. 
 **Core Principles**: Only change `end_time`, never touch `start_time`, never cause overlaps. 
 
 # Inputs 
 {{inputData}} 
 (Includes fields: id, text, start_time, end_time, next_line_start_time) 
 
 # Rules (Simplified Logic) 
 
 Perform the following **Reading Comfort Check** for each line: 
 
 1.  **Density Check** 
     * Observe the length of `text` and current `duration` (`end` - `start`). 
     * If text is long but duration is very short (e.g., over 10 chars but less than 1.5s), mark as **"Rushed"**. 
     * If text is extremely short (e.g., "No.", "Fine.") but time is very long, mark as **"Loose"**, usually no action needed. 
 
 2.  **Gap Filling Strategy** 
     * If judged as **"Rushed"**, check if there is a **Gap** between the current line and the next line. 
     * `Max_End_Time` = `next_line_start_time` - **50ms** (50ms safety buffer). 
     * **Action**: Extend `end_time` to `Max_End_Time` to maximize reading time. 
     * *Note*: If this is the last line (no next_line) and text is long, extend by 1-2 seconds as appropriate. 
 
 3.  **Minimum Duration Constraint** 
     * Except for single words (e.g., "Hi", "Yes"), try to ensure each line stays for at least **1.2 seconds (1200ms)**. 
     * If there is a gap, prioritize meeting the 1.2s duration. 
 
 # Constraints 
 * **Language Agnostic**: Regardless of language, judge based on the visual standard of "looks like a lot of characters." 
 * **Safety First**: Modified `end_time` **strictly forbidden** to be greater than or equal to `next_line_start_time`. 
 
 # Output Requirements 
 1.  **Format**: Pure JSON array, no Markdown. 
 2.  **Data**: Must include `action` ("KEEP" or "EXTEND") and `reason`. 
 
 # Output Example 
 [ 
   { 
     "id": "1", 
     "text": "This implies a significant geopolitical shift.", 
     "original_end": "00:00:02,500", 
     "adjusted_end": "00:00:03,100", 
     "action": "EXTEND", 
     "reason": "Text is long (42 chars), utilizing gap to extend 600ms for better reading experience." 
   }, 
   { 
     "id": "2", 
     "text": "No.", 
     "original_end": "00:00:04,000", 
     "adjusted_end": "00:00:04,000", 
     "action": "KEEP", 
     "reason": "Text is extremely short, current duration is sufficient." 
   } 
 ] 
""";

            return prompt;
        }

        /// <summary>
        /// Format timecode
        /// </summary>
        private static string FormatTimeCode(double totalMs)
        {
            var ts = TimeSpan.FromMilliseconds(totalMs);
            return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";
        }

        /// <summary>
        /// Parse timecode
        /// </summary>
        private static double ParseTimeCode(string timeCode)
        {
            // Format: HH:MM:SS,mmm
            var pattern = @"(\d{2}):(\d{2}):(\d{2})[,.](\d{3})";
            var match = Regex.Match(timeCode, pattern);
            if (match.Success)
            {
                var hours = int.Parse(match.Groups[1].Value);
                var minutes = int.Parse(match.Groups[2].Value);
                var seconds = int.Parse(match.Groups[3].Value);
                var ms = int.Parse(match.Groups[4].Value);
                return new TimeSpan(0, hours, minutes, seconds, ms).TotalMilliseconds;
            }
            return 0;
        }



        /// <summary>
        /// Apply timing adjustments to original subtitle
        /// </summary>
        private void ApplyAdjustments(WorkflowContext context, List<TimingAdjustmentItem> adjustments)
        {
            var paragraphs = _context.Subtitle.Paragraphs;
            context.TimingStatistics = new TimingStatistics();

            foreach (var adjustment in adjustments)
            {
                if (int.TryParse(adjustment.Id, out var index) && index < paragraphs.Count)
                {
                    context.TimingStatistics.TotalProcessed++;

                    if (adjustment.IsExtended)
                    {
                        var newEndMs = ParseTimeCode(adjustment.AdjustedEndTime);
                        var originalEndMs = paragraphs[index].EndTime.TotalMilliseconds;

                        if (newEndMs > originalEndMs)
                        {
                            paragraphs[index].EndTime.TotalMilliseconds = newEndMs;
                            context.TimingStatistics.ExtendedCount++;
                            context.TimingStatistics.TotalExtensionMs += (long)(newEndMs - originalEndMs);
                        }
                        else
                        {
                            context.TimingStatistics.KeptCount++;
                        }
                    }
                    else
                    {
                        context.TimingStatistics.KeptCount++;
                    }
                }
            }
        }
    }

    #region Step6 Result Models

    /// <summary>
    /// Single timing adjustment result
    /// </summary>
    public class TimingAdjustmentItem
    {
        /// <summary>
        /// Subtitle index
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Polished text
        /// </summary>
        [JsonPropertyName("text")]
        public string PolishedText { get; set; } = string.Empty;

        /// <summary>
        /// Original duration (ms)
        /// </summary>
        [JsonPropertyName("original_duration_ms")]
        public int OriginalDurationMs { get; set; }

        /// <summary>
        /// Character count (excluding punctuation)
        /// </summary>
        [JsonPropertyName("char_count")]
        public int CharCount { get; set; }

        /// <summary>
        /// Action type: KEEP or EXTEND
        /// </summary>
        [JsonPropertyName("action")]
        public string Action { get; set; } = "KEEP";

        /// <summary>
        /// Original end time
        /// </summary>
        [JsonPropertyName("original_end")]
        public string OriginalEndTime { get; set; } = string.Empty;

        /// <summary>
        /// Adjusted end time
        /// </summary>
        [JsonPropertyName("adjusted_end")]
        public string AdjustedEndTime { get; set; } = string.Empty;

        /// <summary>
        /// Adjustment reason
        /// </summary>
        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// Whether it was extended
        /// </summary>
        [JsonIgnore]
        public bool IsExtended => Action?.ToUpperInvariant() == "EXTEND";

        /// <summary>
        /// Original CPS
        /// </summary>
        [JsonIgnore]
        public double OriginalCps => OriginalDurationMs > 0
            ? CharCount / (OriginalDurationMs / 1000.0)
            : 0;
    }

    /// <summary>
    /// Timing adjustment statistics
    /// </summary>
    public class TimingStatistics
    {
        /// <summary>
        /// Total processed count
        /// </summary>
        public int TotalProcessed { get; set; }

        /// <summary>
        /// Extended count
        /// </summary>
        public int ExtendedCount { get; set; }

        /// <summary>
        /// Kept unchanged count
        /// </summary>
        public int KeptCount { get; set; }

        /// <summary>
        /// Total extension duration (ms)
        /// </summary>
        public long TotalExtensionMs { get; set; }
    }

    #endregion
}
}