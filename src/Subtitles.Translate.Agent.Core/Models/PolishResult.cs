using System.Text.Json.Serialization;

namespace Subtitles.Translate.Agent.Core.Models;

/// <summary>
/// Polishing statistics
/// </summary>
public class PolishStatistics
{
    /// <summary>
    /// Total lines processed for polishing
    /// </summary>
    public int TotalPolished { get; set; }

    /// <summary>
    /// Number of lines actually modified by polishing
    /// </summary>
    public int ChangeCount { get; set; }

    /// <summary>
    /// Modification rate (%)
    /// </summary>
    public double ModifiedRate => TotalPolished > 0 ? (double)ChangeCount / TotalPolished * 100 : 0;
}
