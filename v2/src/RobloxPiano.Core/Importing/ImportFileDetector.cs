using System.Text;
using System.Text.RegularExpressions;

namespace RobloxPiano.Core.Importing;

public static class ImportFileDetector
{
    public const long MaxMidiSizeBytes = 100 * 1024 * 1024; // 100 MB
    public const long MaxMmlSizeBytes = 20 * 1024 * 1024;   // 20 MB

    private static readonly byte[] MidiHeaderMagic = "MThd"u8.ToArray();

    private static readonly Regex MmlPlausibleRegex = new(
        @"(?:MML@|[A-Ga-g][+#-]?(?:\d+)?|[Nn]-?\d+|[Rr](?:\d+)?|[Ll]\d*|[Oo]\d*|[><]|[Vv]\d*|[Tt]\d*)",
        RegexOptions.Compiled);

    public static (ImportSourceType SourceType, string? ErrorMessage) Detect(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return (ImportSourceType.Unknown, ImportError.FileNotFound);

        if (!File.Exists(filePath))
            return (ImportSourceType.Unknown, ImportError.FileNotFound);

        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length == 0)
            return (ImportSourceType.Unknown, ImportError.EmptyFile);

        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        switch (ext)
        {
            case ".mid" or ".midi":
                if (fileInfo.Length > MaxMidiSizeBytes)
                    return (ImportSourceType.Unknown, ImportError.FileTooLarge);

                return DetectMidi(filePath);

            case ".mml":
                if (fileInfo.Length > MaxMmlSizeBytes)
                    return (ImportSourceType.Unknown, ImportError.FileTooLarge);

                return DetectMmlFile(filePath, isExplicitMmlExtension: true);

            case ".txt":
                if (fileInfo.Length > MaxMmlSizeBytes)
                    return (ImportSourceType.Unknown, ImportError.FileTooLarge);

                return DetectMmlFile(filePath, isExplicitMmlExtension: false);

            default:
                return (ImportSourceType.Unknown, ImportError.UnsupportedFormat);
        }
    }

    private static (ImportSourceType SourceType, string? ErrorMessage) DetectMidi(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] header = new byte[4];
            int read = fs.Read(header, 0, 4);
            if (read < 4 || !header.SequenceEqual(MidiHeaderMagic))
            {
                return (ImportSourceType.Unknown, ImportError.CorruptMidi);
            }

            return (ImportSourceType.Midi, null);
        }
        catch
        {
            return (ImportSourceType.Unknown, ImportError.CorruptMidi);
        }
    }

    private static (ImportSourceType SourceType, string? ErrorMessage) DetectMmlFile(string filePath, bool isExplicitMmlExtension)
    {
        try
        {
            using var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            char[] buffer = new char[512];
            int read = reader.Read(buffer, 0, buffer.Length);
            if (read <= 0)
                return (ImportSourceType.Unknown, ImportError.EmptyFile);

            var sample = new string(buffer, 0, read).Trim();

            if (sample.StartsWith("MML@", StringComparison.OrdinalIgnoreCase))
            {
                return (ImportSourceType.Mml, null);
            }

            if (isExplicitMmlExtension)
            {
                // .mml extension with valid MML tokens
                if (MmlPlausibleRegex.IsMatch(sample))
                {
                    return (ImportSourceType.Mml, null);
                }
                return (ImportSourceType.Unknown, ImportError.InvalidMml);
            }

            // For .txt files, require explicit MML@ header or high-confidence MML structure
            // to avoid false positive on arbitrary text files
            if (sample.StartsWith("MML@", StringComparison.OrdinalIgnoreCase))
            {
                return (ImportSourceType.Mml, null);
            }

            // If .txt does not start with MML@, check if it contains MML@ anywhere in first chunk or ends with semicolon and has MML tokens
            if (sample.Contains("MML@", StringComparison.OrdinalIgnoreCase))
            {
                return (ImportSourceType.Mml, null);
            }

            // Otherwise, .txt is rejected
            return (ImportSourceType.Unknown, ImportError.InvalidMml);
        }
        catch
        {
            return (ImportSourceType.Unknown, ImportError.InvalidMml);
        }
    }
}
