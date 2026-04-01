using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Subtitles.Translate.Agent.Core.Models;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Subtitles.Translate.Agent.Core.Agents
{
    /// <summary>
    /// Step 3: Subtitle Translation Agent
    /// Translates subtitles using a sliding window approach, supporting context reference
    /// </summary>
    public class Step3_TranslatorAgent : AgentBase
    {
        public const string AgentName = nameof(Step3_TranslatorAgent);
        private readonly AIAgent _agent;
        private readonly Step4_ReviewerAgent _reviewerAgent;
        private int _currentBatchIndex;

        public Step3_TranslatorAgent(WorkflowContext context)
            : base(context, AgentName)
        {
            // Create ChatClient and initialize Agent
            var client = CreateChatClient();
            _agent = client.CreateAIAgent().AsBuilder().Build();

            // Initialize Reviewer Agent
            _reviewerAgent = new Step4_ReviewerAgent(context);
        }

        /// <summary>
        /// Execute sliding window translation
        /// </summary>
        public override async Task ExecuteAsync()
        {
            _logger?.LogInformation("===== {AgentName} started execution =====", AgentName);

            var totalLines = _context.Subtitle.Paragraphs.Count;
            var batchSize = _context.Request.BatchSize;

            // Initialize translation results and progress
            _context.TranslatedItems = new List<TranslationItem>();
            _context.TranslationProgress ??= new TranslationProgress
            {
                TotalLines = totalLines,
                TotalBatches = (int)Math.Ceiling((double)totalLines / batchSize)
            };

            _logger?.LogDebug("Total subtitles: {Total}, Batch size: {BatchSize}, Total batches: {TotalBatches}",
                totalLines, batchSize, _context.TranslationProgress.TotalBatches);

            // Sliding window translation loop
            int startIndex = _context.TranslatedItems.Count;
            while (startIndex < totalLines)
            {
                _currentBatchIndex = _context.TranslationProgress.CurrentBatchIndex;
                int batchEnd = Math.Min(startIndex + batchSize, totalLines);

                // Batch translation with retry
                List<TranslationItem>? translations = null;
                int maxRetries = _context.MaxRetries;
                int retryCount = 0;
                Exception? lastException = null;

                while (retryCount <= maxRetries)
                {
                    try
                    {
                        translations = await ProcessBatchAsync(startIndex, batchEnd);
                        retryCount = 0;
                        break; // Break retry loop on success
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        retryCount++;

                        if (retryCount <= maxRetries)
                        {
                            _logger?.LogWarning(ex, "Batch {BatchIndex} translation failed, retrying {RetryCount}/{MaxRetries}...",
                                _currentBatchIndex, retryCount, maxRetries);

                            // Exponential backoff delay to avoid immediate retry
                            int delayMs = Math.Min(1000 * (int)Math.Pow(2, retryCount - 1), 30000);
                            await Task.Delay(delayMs);
                        }
                        else
                        {
                            _logger?.LogError(ex, "Batch {BatchIndex} translation failed, reached max retries {MaxRetries}",
                                _currentBatchIndex, maxRetries);
                            throw new InvalidOperationException(
                                $"Batch {_currentBatchIndex} (Subtitle {startIndex}-{batchEnd - 1}) translation failed, retried {maxRetries} times. Error: {ex.Message}", ex);
                        }
                    }
                }

                if (translations != null)
                {
                    LogBatchDetails(translations);
                    _context.TranslatedItems.AddRange(translations);
                }

                // Update progress
                _context.TranslationProgress.TranslatedLines = _context.TranslatedItems.Count;
                _context.TranslationProgress.CurrentBatchIndex++;
                startIndex = batchEnd;
            }

            _logger?.LogInformation("===== {AgentName} execution completed, translated {Count} subtitles =====",
                AgentName, _context.TranslatedItems.Count);
        }

        /// <summary>
        /// Process a single batch translation workflow
        /// </summary>
        private async Task<List<TranslationItem>?> ProcessBatchAsync(int startIndex, int batchEnd)
        {
            int batchSize = batchEnd - startIndex;
            _logger?.LogInformation("Processing batch {BatchIndex}: Subtitles {Start}-{End} (Total {Count} items)",
                _currentBatchIndex, startIndex, batchEnd - 1, batchSize);

            // Build batch data and generate prompt
            var batchData = BuildBatchData(startIndex, batchEnd);
            var prompt = GetPrompt(batchData);
            _logger?.LogDebug("Prompt /n:{prompt}", prompt);
            _logger?.LogDebug("Prompt length: {Length} chars", prompt.Length);

            // Call AI translation
            _logger?.LogInformation("Calling LLM (Model: {ModelId})...", _agentConfig.ModelId);
            var response = await _agent.RunAsync(prompt);
            RecordTokenUsage(AgentName, response.Usage, _currentBatchIndex);
            _logger?.LogDebug("Token usage - Input: {Input}, Output: {Output}",
                response.Usage?.InputTokenCount ?? 0, response.Usage?.OutputTokenCount ?? 0);

            // Parse and verify results
            var rawResults = ParseJsonResponse<List<Step3_TranslateResult>>(response.Text);



            // Verify translation result quantity

            if (rawResults == null || rawResults.Count == 0 || rawResults.Count != batchSize)
            {
                throw new InvalidOperationException(
                    $"Translation result count mismatch. Expected: {batchSize}, Actual: {rawResults?.Count ?? 0}. Batch range: {startIndex}-{batchEnd - 1}");
            }

            // Convert to TranslationItem
            var translations = rawResults.Select(r => new TranslationItem
            {
                Id = r.Id,
                Original = r.Original,
                InitialTranslation = r.InitialTranslation
            }).ToList();

            // Execute review
            if (_context.Request.EnableReview)
            {
                translations = await ExecuteReviewAsync(translations);
            }

            return translations;
        }

        /// <summary>
        /// Execute review workflow
        /// </summary>
        private async Task<List<TranslationItem>> ExecuteReviewAsync(List<TranslationItem> translations)
        {
            _logger?.LogDebug("Executing review...");
            _reviewerAgent.SetBatchIndex(_currentBatchIndex);

            var reviewedItems = await _reviewerAgent.ReviewBatchAsync(_context, translations);

            // Verify reviewed result count matches
            if (translations.Count != reviewedItems.Count)
            {
                throw new InvalidOperationException(
                    $"Review result count mismatch: Expected {translations.Count}, Actual {reviewedItems.Count}");
            }

            // Update statistics
            _context.ReviewStatistics.TotalReviewed += reviewedItems.Count;
            _context.ReviewStatistics.FixedCount += reviewedItems.Count(r => r.IsFixed);
            _context.ReviewStatistics.PassedCount += reviewedItems.Count(r => !r.IsFixed);

            // Update translation results with reviewed translations, and save review details
            foreach (var (translation, review) in translations.Zip(reviewedItems))
            {
                translation.ReviewStatus = review.Status;
                translation.ReviewCritique = review.Critique;
                translation.ReviewFinalTranslation = review.FinalTranslation;
            }

            _logger?.LogDebug("Review completed, fixed {Fixed} items, passed {Passed} items",
                reviewedItems.Count(r => r.IsFixed), reviewedItems.Count(r => !r.IsFixed));

            return translations;
        }

        /// <summary>
        /// Build current batch data
        /// </summary>
        private BatchData BuildBatchData(int startIndex, int endIndex)
        {
            var paragraphs = _context.Subtitle.Paragraphs;
            var request = _context.Request;

            // Preceding context (translated)
            var precedingStart = Math.Max(0, startIndex - request.PrecedingContextLines);
            var precedingContext = Enumerable.Range(precedingStart, startIndex - precedingStart)
                .Select(i => (i, paragraphs[i].Text, _context.TranslatedItems?.ElementAtOrDefault(i)?.InitialTranslation))
                .ToList();

            // Current batch
            var currentBatch = Enumerable.Range(startIndex, endIndex - startIndex)
                .Select(i => (i, paragraphs[i].Text))
                .ToList();

            // Following context (untranslated preview)
            var followingEnd = Math.Min(paragraphs.Count, endIndex + request.FollowingContextLines);
            var followingContext = Enumerable.Range(endIndex, followingEnd - endIndex)
                .Select(i => (i, paragraphs[i].Text))
                .ToList();

            return new BatchData
            {
                PrecedingContext = precedingContext,
                CurrentBatch = currentBatch,
                FollowingContext = followingContext
            };
        }

        /// <summary>
        /// Generate translation prompt
        /// </summary>
        private string GetPrompt(BatchData batchData)
        {
            var request = _context.Request;

            // Build context strings
            var precedingContextStr = FormatContextLines(batchData.PrecedingContext);
            var currentBatchStr = FormatBatchLines(batchData.CurrentBatch);
            var followingContextStr = FormatBatchLines(batchData.FollowingContext);

            var prompt = $$"""
# Role
You are a senior film and television subtitle translation expert proficient in multiple languages. You possess strong context awareness and can read project documents to translate scattered subtitle fragments into fluent, natural translations that fit the target audience's habits.

# Task
Based on the provided **[Style Guide]**, **[Glossary]**, and **[Dialogue Context]**, translate the current **[Subtitle Batch]** into the target language.

# Source Language
**Auto-Detect (Please detect source language based on input text)**

# Target Language
**{{request.TargetLanguage}}**

# Inputs (Reference Documents)

## 1. Style Guide
*Document Content*:
{{_context.Step1_DirectorResult!.ToMarkdown()}}

*Instructions*:
- **Tone**: Read "Overall Tone" carefully, ensure the translation fits the emotional color (e.g., serious, humorous, confrontational).
- **Style**: Strictly follow "Style Instructions" (e.g., news broadcast style, colloquialism level).
- **Address**: Refer to "Address Strategy" to clarify relationships and address strategies between characters/speakers.


## 2. Glossary
*Document Content*:
{{_context.Step2_GlossaryResult!.ToMarkdown()}}

*CRITICAL INSTRUCTION*:
- **Characters**: Look up names in `- Character Mapping`. Must use the translated name specified in the document. Use pronouns (he/she) accurately based on Gender in remarks.
- **Terms**: Look up proper nouns in `- Place Name Mapping` and `- Terminology Table`. If the original text appears in the list, **you must use** the corresponding translated name in the document, and are strictly forbidden from improvising.

## 3. Context Stream
- **Preceding Context (Translated)**:
{{precedingContextStr}}
*(Used to maintain tone continuity, do not re-translate this part)*

- **Following Context (Preview)**:
{{followingContextStr}}
*(For reference only to eliminate ambiguity, **absolutely do not** translate this part)*

## 4. Current Subtitle Batch
{{currentBatchStr}}

# Translation Process Rules

1.  **Semantic Fusion**:
    - Subtitles are often cut by the timeline (cross-line sentence breaking).
    - **Must** first piece together multiple lines of original text into a complete sentence in your mind, understand the full meaning, translate, and then split back to the corresponding lines according to the original ID.
    - *Prohibit* word-for-word translation (e.g., Line 1 "I decided to" -> "我决定去", Line 2 "give up" -> "放弃" ——> Should be optimized to Line 1 "我决定", Line 2 "放弃了").

2.  **Contextual Adaptation**:
    - Combine background information from Step 1 to identify subtext.
    - If the original text contains pronouns (It/He/They), must combine context to clarify the object and avoid ambiguity.

3.  **Format Integrity**:
    - Translation length should fit the original duration as much as possible to avoid reading difficulties caused by excessive length.
    - Punctuation marks must comply with target language norms.

# Output Requirements

1.  **Structure**: Output pure JSON array, strictly forbid including Markdown (` ```json `) or other explanatory text.
2.  **Quantity Check**: The number of array elements returned must be exactly the same as `Current Subtitle Batch` (**Exactly {{batchData.CurrentBatch.Count}} items**, ID range: {{batchData.CurrentBatch.First().Index}} to {{batchData.CurrentBatch.Last().Index}}).
3.  **Format**:
[
  {
    "id": "Keep original ID",
    "original": "Original content (kept for verification)",
    "initial_translation": "Final translation"
  }
]
""";

            return prompt;
        }

        /// <summary>
        /// Format preceding context (includes original and translation)
        /// </summary>
        private static string FormatContextLines(List<(int Index, string Original, string? Translation)> lines)
        {
            var sb = new StringBuilder();
            foreach (var (index, original, translation) in lines)
            {
                sb.AppendLine($"[{index}] {original}");
                sb.AppendLine($"    → {translation ?? "(Untranslated)"}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Format batch/following context (original only)
        /// </summary>
        private static string FormatBatchLines(List<(int Index, string Text)> lines)
        {
            var sb = new StringBuilder();
            foreach (var (index, text) in lines)
                sb.AppendLine($"[{index}] {text}");
            return sb.ToString();
        }

        /// <summary>
        /// Log batch translation details
        /// </summary>
        private void LogBatchDetails(List<TranslationItem> translations)
        {
            if (_logger == null || translations == null || translations.Count == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine($"\n===== Batch {_currentBatchIndex} Translation Details =====");

            foreach (var item in translations)
            {
                sb.AppendLine($"ID: {item.Id}");
                sb.AppendLine($"Original: {item.Original}");

                // Review info
                if (!string.IsNullOrEmpty(item.ReviewStatus)
                    && item.ReviewStatus != "PASS")
                {
                    sb.AppendLine($"Initial: {item.InitialTranslation ?? item.Translation}"); // If no InitialTranslation (should not happen theoretically), fallback to Translation
                    sb.AppendLine($"Review [{item.ReviewStatus}]: {item.ReviewCritique ?? "No comment"}");
                    sb.AppendLine($"Post-Review: {item.ReviewFinalTranslation}");
                }

                sb.AppendLine($"Final Translation: {item.Translation}");
                sb.AppendLine(new string('-', 30));
            }

            _logger.LogDebug(sb.ToString());
        }


    }

    /// <summary>
    /// Batch data structure
    /// </summary>
    public class BatchData
    {
        /// <summary>
        /// Preceding context (translated)
        /// </summary>
        public List<(int Index, string Original, string? Translation)> PrecedingContext { get; set; } = new();

        /// <summary>
        /// Current batch to translate
        /// </summary>
        public List<(int Index, string Text)> CurrentBatch { get; set; } = new();

        /// <summary>
        /// Following context (untranslated, preview only)
        /// </summary>
        public List<(int Index, string Text)> FollowingContext { get; set; } = new();
    }

    /// <summary>
    /// Step 3 translation result entity
    /// </summary>
    public class Step3_TranslateResult
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("original")]
        public string Original { get; set; } = string.Empty;

        [JsonPropertyName("initial_translation")]
        public string InitialTranslation { get; set; } = string.Empty;
    }
}

