namespace RobloxPiano.Core.Importing;

public class SmartMmlResult
{
    public bool Success { get; }
    public string ProcessedMml { get; }
    public string? ExtractedTitle { get; }
    public int TrackCount { get; }
    public bool HasModifications { get; }
    public IReadOnlyList<string> Diagnostics { get; }
    public string? ErrorMessage { get; }

    public SmartMmlResult(
        bool success,
        string processedMml,
        string? extractedTitle = null,
        int trackCount = 1,
        bool hasModifications = false,
        IReadOnlyList<string>? diagnostics = null,
        string? errorMessage = null)
    {
        Success = success;
        ProcessedMml = processedMml;
        ExtractedTitle = extractedTitle;
        TrackCount = trackCount;
        HasModifications = hasModifications;
        Diagnostics = diagnostics ?? Array.Empty<string>();
        ErrorMessage = errorMessage;
    }

    public static SmartMmlResult Succeeded(
        string processedMml,
        string? extractedTitle = null,
        int trackCount = 1,
        bool hasModifications = false,
        IReadOnlyList<string>? diagnostics = null)
    {
        return new SmartMmlResult(true, processedMml, extractedTitle, trackCount, hasModifications, diagnostics);
    }

    public static SmartMmlResult Failed(string rawInput, string errorMessage)
    {
        return new SmartMmlResult(false, rawInput, null, 0, false, null, errorMessage);
    }
}
