using Nikse.SubtitleEdit.Core.Common;
using System.Text;

namespace Subtitles.Translate.Agent.Core.Utils;

/// <summary>
/// Subtitle formatting utility class
/// </summary>
public static class SubtitleFormatUtility
{
    /// <summary>
    /// Converts Subtitle to custom format:
    /// [StartSeconds]SubtitleContent
    /// [StartSeconds]SubtitleContent
    /// </summary>
    /// <param name="subtitle">Subtitle object</param>
    /// <returns>Formatted string</returns>
    public static string ToCustomFormat(Subtitle subtitle)
    {
        if (subtitle == null || subtitle.Paragraphs == null)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var p in subtitle.Paragraphs)
        {
            // Remove newlines to ensure each subtitle occupies one line for easier processing
            // If newlines need to be preserved, adjust as needed, but this compact format usually prefers single lines
            var text = p.Text.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
            
            // Use 0.### format for seconds to keep necessary decimal places
            sb.AppendLine($"[{p.StartTime.TotalSeconds:0.###}]{text}");
        }
        return sb.ToString();
    }
}
