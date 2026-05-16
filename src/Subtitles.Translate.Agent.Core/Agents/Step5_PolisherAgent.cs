using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Subtitles.Translate.Agent.Core.Models;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Subtitles.Translate.Agent.Core.Agents
{
    /// <summary>
    /// Step 5: Terminology Compliance + Subtitle Polishing Agent
    /// Responsible for mandatory terminology replacement, pronoun gender correction, and final polishing (minimalist, idiomatic, rhythmic)
    /// </summary>
    public class Step5_PolisherAgent : AgentBase
    {
        public const string AgentName = nameof(Step5_PolisherAgent);
        private readonly AIAgent _agent;
        private int _currentBatchIndex;

        public Step5_PolisherAgent(WorkflowContext context)
            : base(context, AgentName)
        {
            var client = CreateChatClient();
            _agent = client.CreateAIAgent().AsBuilder().Build();
        }

        /// <summary>
        /// Execute sliding window polishing
        /// </summary>
        public override async Task ExecuteAsync()
        {
            _logger?.LogInformation("===== {AgentName} started execution =====", AgentName);

            var totalLines = _context.TranslatedItems.Count;
            var batchSize = _context.Request.BatchSize;

            if (totalLines == 0)
            {
                _logger?.LogWarning("No translation results to polish");
                return;
            }

            // Initialize progress
            var totalBatches = (int)Math.Ceiling((double)totalLines / batchSize);
            _logger?.LogDebug("Total subtitles: {Total}, Batch size: {BatchSize}, Total batches: {TotalBatches}",
                totalLines, batchSize, totalBatches);

            // Accumulate polished results (as context for subsequent batches)
            var allPolishedItems = new List<Step5_PolisherResult>();

            // Sliding window polishing loop
            int startIndex = 0;
            _currentBatchIndex = 0;
            while (startIndex < totalLines)
            {
                int batchEnd = Math.Min(startIndex + batchSize, totalLines);
                int batchCount = batchEnd - startIndex;

                _logger?.LogInformation("Processing batch {BatchIndex}: Subtitles {Start}-{End} (Total {Count})",
                    _currentBatchIndex, startIndex, batchEnd - 1, batchCount);

                // Get current batch translations (from Step 3/4 results)
                var currentBatch = _context.TranslatedItems
                    .Skip(startIndex)
                    .Take(batchCount)
                    .ToList();

                // Get preceding polished results as context
                var precedingPolished = allPolishedItems.Count > 0
                    ? allPolishedItems.TakeLast(_context.Request.PrecedingContextLines).ToList()
                    : null;

                // Batch polishing with retry
                List<Step5_PolisherResult>? polishedItems = null;
                int maxRetries = _context.MaxRetries;
                int retryCount = 0;

                while (retryCount <= maxRetries)
                {
                    try
                    {
                        polishedItems = await PolishBatchAsync(_context, currentBatch, precedingPolished);

                        // Verify matching number of results
                        if (polishedItems == null || currentBatch.Count != polishedItems.Count)
                        {
                            throw new InvalidOperationException(
                                $"Polishing result count mismatch: Expected {currentBatch.Count}, Actual {polishedItems?.Count ?? 0}");
                        }

                        break; // Success
                    }
                    catch (Exception ex)
                    {
                        retryCount++;

                        if (retryCount <= maxRetries)
                        {
                            _logger?.LogWarning(ex, "Batch {BatchIndex} polishing failed, retrying {RetryCount}/{MaxRetries}...",
                                _currentBatchIndex, retryCount, maxRetries);

                            await Task.Delay(1000 * retryCount);
                        }
                        else
                        {
                            _logger?.LogError(ex, "Batch {BatchIndex} polishing failed after {MaxRetries} retries. Skipping polishing for this batch.",
                                _currentBatchIndex, maxRetries);

                            // Fallback: Use original translation
                            polishedItems = currentBatch.Select(x => new Step5_PolisherResult
                            {
                                Id = x.Id,
                                Original = x.Original,
                                Translation = x.Translation,
                                PolishedText = null,
                                Note = $"Polishing failed: {ex.Message}",
                                OptimizationTag = "SKIPPED"
                            }).ToList();

                            break;
                        }
                    }
                }

                if (polishedItems == null)
                {
                    startIndex = batchEnd;
                    _currentBatchIndex++;
                    continue;
                }

                // Update statistics
                _context.PolishStatistics.TotalPolished += polishedItems.Count;
                _context.PolishStatistics.ChangeCount += polishedItems.Count(x => !string.IsNullOrEmpty(x.PolishedText));

                // Update translation items with polished text and notes
                foreach (var (translation, polish) in currentBatch.Zip(polishedItems))
                {
                    translation.PolishText = polish.PolishedText;
                    translation.PolishNote = polish.Note;
                    translation.PolishOptimizationTag = polish.OptimizationTag;
                }

                // Log batch details
                LogBatchDetails(currentBatch);

                // Accumulate polished results
                allPolishedItems.AddRange(polishedItems);

                // Update progress
                startIndex = batchEnd;
                _currentBatchIndex++;

                // Progress reporting removed to match open-source infrastructure
                // double progressRatio = (double)startIndex / totalLines;
                // int mappedProgress = 60 + (int)(progressRatio * 30);
                // await ReportProgressAsync(mappedProgress, "Step5_Polishing");
            }

            _logger?.LogInformation("===== {AgentName} completed, polished {Count} subtitles =====",
                AgentName, totalLines);
        }

        /// <summary>
        /// Log batch polishing details
        /// </summary>
        private void LogBatchDetails(List<TranslationItem> translations)
        {
            if (_logger == null || translations == null || translations.Count == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine($"\n===== Batch {_currentBatchIndex} Polishing Details =====");

            foreach (var item in translations)
            {
                if (!string.IsNullOrWhiteSpace(item.PolishText))
                {
                    sb.AppendLine($"ID: {item.Id}");
                    sb.AppendLine($"Original: {item.Original}");
                    sb.AppendLine($"Initial: {item.InitialTranslation}");
                    sb.AppendLine($"Review: {item.ReviewFinalTranslation}");
                    sb.AppendLine($"Polished: {item.PolishText}");
                    sb.AppendLine($"Tag [{item.PolishOptimizationTag}]: {item.PolishNote ?? "None"}");
                    sb.AppendLine(new string('-', 30));
                }
            }

            _logger.LogInformation(sb.ToString());
        }

        /// <summary>
        /// Polish a batch of translations
        /// </summary>
        public async Task<List<Step5_PolisherResult>> PolishBatchAsync(
            WorkflowContext context,
            List<TranslationItem> currentBatch,
            List<Step5_PolisherResult>? precedingPolished = null)
        {
            _logger?.LogDebug("Polishing batch {BatchIndex}, {Count} lines", _currentBatchIndex, currentBatch.Count);

            var prompt = GetPrompt(context, currentBatch, precedingPolished);

            _logger?.LogDebug("Prompt length: {Length} chars", prompt.Length);
            _logger?.LogDebug("Calling LLM (Model: {ModelId})...", _agentConfig.ModelId);

            var response = await _agent.RunAsync(prompt);

            RecordTokenUsage(AgentName, response.Usage, _currentBatchIndex);

            _logger?.LogDebug("Token Usage - Input: {Input}, Output: {Output}",
                response.Usage?.InputTokenCount ?? 0, response.Usage?.OutputTokenCount ?? 0);
            
            var polishedItems = ParseJsonResponse<List<Step5_PolisherResult>>(response.Text);

            if (polishedItems == null || polishedItems.Count != currentBatch.Count)
            {
                _logger?.LogWarning("Batch {BatchIndex} parsing failed or count mismatch. Expected: {Expected}, Actual: {Actual}",
                    _currentBatchIndex, currentBatch.Count, polishedItems?.Count ?? 0);
                throw new Exception($"Batch {_currentBatchIndex} polishing parsing failed or count mismatch.");
            }

            return polishedItems;
        }

        /// <summary>
        /// Generate polishing prompt
        /// </summary>
        public string GetPrompt(
            WorkflowContext context,
            List<TranslationItem> currentBatch,
            List<Step5_PolisherResult>? precedingPolished)
        {
            var request = context.Request;

            // Get Step 2 Glossary Markdown
            var step2GlossaryMarkdown = context.Step2_GlossaryResult?.ToMarkdown() ?? "(No glossary)";

            // Build preceding polished results string
            var precedingStr = new StringBuilder();
            if (precedingPolished != null && precedingPolished.Count > 0)
            {
                var contextLines = precedingPolished.TakeLast(request.PrecedingContextLines);
                foreach (var item in contextLines)
                {
                    var finalTxt = item.PolishedText ?? item.Translation;
                    var text = finalTxt.Replace("\r", "").Replace("\n", " ");
                    precedingStr.AppendLine($"[{item.Id}] {text}");
                }
            }

            // Build current batch JSON
            var currentBatchJson = JsonSerializer.Serialize(
                currentBatch.Select(t => new
                {
                    id = t.Id,
                    original = t.Original,
                    translation = t.Translation
                }).ToList(),
                DefaultJsonOptions);

            var prompt = $$""""
# Role
You are a top-tier Subtitle Editor and **Terminology Compliance Officer**. Your dual responsibilities are:
1. **Mandatory Terminology Enforcement**: Ensure all proper nouns use the preset translations, with the highest priority.
2. **Flow Polishing**: While **strictly adhering to the original timing/cuts**, polish the subtitles to be "idiomatic, rhythmic, and punchy."

# Task
Based on the **[Glossary]**, **[Style Guide]**, **[Translation Results]**, and **[Preceding Polished Results]**, perform for the current batch:
- **Phase 1**: Terminology compliance correction (mandatory replacement)
- **Phase 2**: Expression polishing (optimizing wording while keeping semantic group break points unchanged)

# Target Language
**{{request.TargetLanguage}}**
*CRITICAL*: Regardless of the prompt language, the `polished_text` field MUST strictly use **{{request.TargetLanguage}}**. If no modification is needed, return null.

# Inputs

## 1. Glossary Document - **Highest Priority**
*Document Content*:
{{step2GlossaryMarkdown}}
*CRITICAL*:
- **Characters**: Look for `- Character Mapping`. MUST use the specified translation. Correct pronouns (he/she) based on `Gender`.
- **Terms**: Look for `- Terminology Map`. FORCED to use the names defined in the table.

## 2. Style Guide Document
*Document Content*:
- **Style Instructions**: {{_context.Step1_DirectorResult!.StyleInstruction}}
- **Pronoun Rules**: {{_context.Step1_DirectorResult!.PronounRules}}
*Focus*: Ensure the polished tone matches the content type.

## 3. Preceding Polished Lines (Context)
{{precedingStr}}
*Usage*: Refer to the flow rhythm and logical connection. **Do not re-process** this part.

## 4. Current Batch (To be processed)
{{currentBatchJson}}

# Processing Pipeline

## Phase 1: Glossary Enforcement
For each line, perform these high-priority checks:
1. **Scan and Replace**: Check `original` and `translation`. If the original text involves terms from [Input 1], **FORCE** the translation to be replaced with the standard name defined in the table.
2. **Hard Constraint**: Even if the replacement makes the sentence slightly stiff, do **NOT** modify the term itself.
3. **Consistency**: Unified pronoun correction (he/she) based on the `Gender` attribute in Input 1.

## Phase 2: Expression Polishing
Based on terminology compliance, perform the following optimizations. The core principle is "flow continuity" rather than "single-line completeness":

1. **Enjambment Preservation (Semantic Cross-line Protection)**:
    * **Strict Rule**: **FORBIDDEN** to move content words (nouns, verbs) from the next line to the current line just to form a complete sentence.
    * **Strategy**: If the current line is half a sentence, use particles or connectors to make it "hover" naturally, waiting for the next line to complete.
    * *Case Study*:
        * *Original*: [1] "a new strike" [2] "on an alleged drug boat"
        * *Bad (Over-merging)*: [1] "A new strike on a drug boat" [2] "(Empty/Particles)" -> **FORBIDDEN! This breaks the timeline.**
        * *Good (Hovering)*: [1] "A new strike" [2] "Against a suspected drug boat" -> **PASS**.

2. **Contextual Flow**:
    * **Action**: Treat `Preceding Lines` + `Current Batch` as a continuous text stream.
    * **Check**: Ensure the "junctions" are smooth.
    * *Example*: If the previous line ends with "performed a", the current line should not repeat the verb, just the object.

3. **Stylization**:
    * Adjust tone according to Input 2 `Style Guide`.
    * *News/Formal*: Keep inverted pyramid structure, use formal connectors, avoid oral fillers.

# Output Requirements

1. **Format**: Pure JSON array, strictly no Markdown tags.
2. **Language**: `polished_text` content MUST be strictly in **{{request.TargetLanguage}}**. If no polishing is needed based on `translation`, `polished_text` MUST return `null`.
3. **Count**: MUST return **exactly {{currentBatch.Count}} items**.
4. **optimization_tag Description**:
    - `"Terminology Correction"`: Only corrected terms/pronouns
    - `"Contextual Polishing"`: Adjusted flow with preceding text
    - `"Style Adaptation"`: Tone or idiomatic optimization
    - `""` or `null`: No change
5. **Structure**:
[
  {
    "id": "Subtitle index, string",
    "original": "Original text",
    "translation": "Original translation",
    "polished_text": "Final result (MUST be in {{request.TargetLanguage}}). If no polish needed, return null",
    "note": "Terminology explanation/slang note (optional, Use Chinese or Target Language)",
    "optimization_tag": "Terminology Correction / Contextual Polishing / Style Adaptation / null"
  }
]
"""";

            return prompt;
        }
    }

    public class Step5_PolisherResult
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("original")]
        public string Original { get; set; } = string.Empty;

        [JsonPropertyName("translation")]
        public string Translation { get; set; } = string.Empty;

        [JsonPropertyName("polished_text")]
        public string? PolishedText { get; set; }

        [JsonPropertyName("note")]
        public string? Note { get; set; }

        [JsonPropertyName("optimization_tag")]
        public string? OptimizationTag { get; set; }
    }
}
