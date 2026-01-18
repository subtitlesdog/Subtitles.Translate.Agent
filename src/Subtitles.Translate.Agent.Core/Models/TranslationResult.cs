using System.Text.Json.Serialization;

namespace Subtitles.Translate.Agent.Core.Models;

/// <summary>
/// Single subtitle translation result
/// </summary>
public class TranslationItem
{
    /// <summary>
    /// Subtitle index/ID
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Original content
    /// </summary>
    [JsonPropertyName("original")]
    public string Original { get; set; } = string.Empty;

    /// <summary>
    /// Translation content (initial)
    /// </summary>
    [JsonPropertyName("translation")]
    public string Translation => PolishedTranslation
        ?? ReviewedTranslation ?? InitialTranslation ?? "";

    /// <summary>
    /// Initial translation backup (for logging and comparison)
    /// </summary>
    [JsonPropertyName("initial_translation")]
    public string? InitialTranslation { get; set; }

    /// <summary>
    /// Reviewed content (Step 4 review result)
    /// </summary>
    [JsonPropertyName("reviewed_translation")]
    public string? ReviewedTranslation { get; set; }

    /// <summary>
    /// Polished content (Step 5 polish result)
    /// </summary>
    [JsonPropertyName("polished_translation")]
    public string? PolishedTranslation { get; set; }

    #region Review related properties (from ReviewItem)

    /// <summary>
    /// Review status: PASS or FIXED
    /// </summary>
    [JsonPropertyName("review_status")]
    public string? ReviewStatus { get; set; }

    /// <summary>
    /// Review modification reason (e.g. terminology error/improper tone), empty if PASS
    /// </summary>
    [JsonPropertyName("review_critique")]
    public string? ReviewCritique { get; set; }

    /// <summary>
    /// Final translation adopted after review
    /// </summary>
    [JsonPropertyName("review_final_translation")]
    public string? ReviewFinalTranslation { get; set; }


    #endregion

    #region Polish related properties (from PolishItem)

    /// <summary>
    /// Final result after polishing
    /// </summary>
    [JsonPropertyName("polish_text")]
    public string? PolishText { get; set; }

    /// <summary>
    /// Slang explanation/domestication note (optional)
    /// </summary>
    [JsonPropertyName("polish_note")]
    public string? PolishNote { get; set; }

    /// <summary>
    /// Optimization tags: Terminology Correction | Conciseness | Coherence Correction | Stylization
    /// </summary>
    [JsonPropertyName("polish_optimization_tag")]
    public string? PolishOptimizationTag { get; set; }

    /// <summary>
    /// Whether it was modified by polishing
    /// </summary>
    [JsonIgnore]
    public bool IsPolishModified => !string.IsNullOrEmpty(PolishOptimizationTag);

    #endregion
}

/// <summary>
/// Translation progress tracking
/// </summary>
public class TranslationProgress
{
    /// <summary>
    /// Total subtitle lines
    /// </summary>
    public int TotalLines { get; set; }

    /// <summary>
    /// Translated lines
    /// </summary>
    public int TranslatedLines { get; set; }

    /// <summary>
    /// Current batch index (starts from 0)
    /// </summary>
    public int CurrentBatchIndex { get; set; }

    /// <summary>
    /// Total batches
    /// </summary>
    public int TotalBatches { get; set; }

    /// <summary>
    /// Is completed
    /// </summary>
    public bool IsCompleted => TranslatedLines >= TotalLines;

    /// <summary>
    /// Completion percentage
    /// </summary>
    public double ProgressPercent => TotalLines > 0 ? (double)TranslatedLines / TotalLines * 100 : 0;
}

