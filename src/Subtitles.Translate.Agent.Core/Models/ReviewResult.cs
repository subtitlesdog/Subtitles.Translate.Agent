using System.Text.Json.Serialization;

namespace Subtitles.Translate.Agent.Core.Models;

/// <summary>
/// Single translation review result
/// </summary>
public class ReviewItem
{
    /// <summary>
    /// Subtitle ID
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Original text
    /// </summary>
    [JsonPropertyName("original")]
    public string Original { get; set; } = string.Empty;

    /// <summary>
    /// Step 3 draft
    /// </summary>
    [JsonPropertyName("draft")]
    public string Draft { get; set; } = string.Empty;

    /// <summary>
    /// Status: PASS or FIXED
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "PASS";

    /// <summary>
    /// Modification reason (e.g. terminology error/improper tone), empty if PASS
    /// </summary>
    [JsonPropertyName("critique")]
    public string Critique { get; set; } = string.Empty;

    /// <summary>
    /// Final translation adopted after review
    /// </summary>
    [JsonPropertyName("final_translation")]
    public string FinalTranslation { get; set; } = string.Empty;

    /// <summary>
    /// Whether it was fixed
    /// </summary>
    [JsonIgnore]
    public bool IsFixed => Status?.ToUpperInvariant() == "FIXED";
}

/// <summary>
/// Review statistics
/// </summary>
public class ReviewStatistics
{
    /// <summary>
    /// Total reviewed count
    /// </summary>
    public int TotalReviewed { get; set; }

    /// <summary>
    /// Passed count (no modification needed)
    /// </summary>
    public int PassedCount { get; set; }

    /// <summary>
    /// Fixed count
    /// </summary>
    public int FixedCount { get; set; }

    /// <summary>
    /// Fixed rate
    /// </summary>
    public double FixedRate => TotalReviewed > 0 ? (double)FixedCount / TotalReviewed * 100 : 0;

    /// <summary>
    /// Get statistics summary
    /// </summary>
    public string GetSummary()
    {
        return $"Reviewed: {TotalReviewed}, Passed: {PassedCount}, Fixed: {FixedCount} ({FixedRate:F1}%)";
    }
}
