using System.Text;
using UtfUnknown;

namespace Subtitles.Translate.Agent.Core.Utils;

/// <summary>
/// File encoding detection and reading utility class
/// Uses UtfUnknown library for encoding detection (dependency of libse)
/// </summary>
public static class FileEncodingUtility
{
    /// <summary>
    /// Detect file encoding format
    /// </summary>
    /// <param name="filePath">File path</param>
    /// <returns>Detected encoding</returns>
    public static Encoding DetectEncoding(string filePath)
    {
        // Use UtfUnknown library for encoding detection
        var result = CharsetDetector.DetectFromFile(filePath);
        
        if (result.Detected != null && result.Detected.Encoding != null)
        {
            return result.Detected.Encoding;
        }
        
        // If UtfUnknown cannot detect, use BOM detection as fallback
        return DetectEncodingByBom(filePath);
    }

    /// <summary>
    /// Detect file encoding via BOM (Fallback method)
    /// </summary>
    private static Encoding DetectEncodingByBom(string filePath)
    {
        byte[] bom = new byte[4];
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            int bytesRead = fs.Read(bom, 0, 4);
            if (bytesRead < 2)
            {
                return Encoding.UTF8;
            }
        }

        // UTF-32 LE BOM: FF FE 00 00
        if (bom[0] == 0xFF && bom[1] == 0xFE && bom[2] == 0x00 && bom[3] == 0x00)
        {
            return Encoding.UTF32;
        }
        // UTF-32 BE BOM: 00 00 FE FF
        if (bom[0] == 0x00 && bom[1] == 0x00 && bom[2] == 0xFE && bom[3] == 0xFF)
        {
            return new UTF32Encoding(bigEndian: true, byteOrderMark: true);
        }
        // UTF-8 BOM: EF BB BF
        if (bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
        {
            return Encoding.UTF8;
        }
        // UTF-16 LE BOM: FF FE
        if (bom[0] == 0xFF && bom[1] == 0xFE)
        {
            return Encoding.Unicode;
        }
        // UTF-16 BE BOM: FE FF
        if (bom[0] == 0xFE && bom[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode;
        }

        // Default to UTF-8
        return Encoding.UTF8;
    }

    /// <summary>
    /// Read file content using detected encoding
    /// </summary>
    /// <param name="filePath">File path</param>
    /// <returns>File content</returns>
    public static string ReadFileWithDetectedEncoding(string filePath)
    {
        var encoding = DetectEncoding(filePath);
        return File.ReadAllText(filePath, encoding);
    }

    /// <summary>
    /// Read file content using detected encoding, and return the used encoding
    /// </summary>
    /// <param name="filePath">File path</param>
    /// <param name="detectedEncoding">Detected encoding</param>
    /// <returns>File content</returns>
    public static string ReadFileWithDetectedEncoding(string filePath, out Encoding detectedEncoding)
    {
        detectedEncoding = DetectEncoding(filePath);
        return File.ReadAllText(filePath, detectedEncoding);
    }
}
