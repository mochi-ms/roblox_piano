namespace RobloxPiano.Infrastructure.Transcription;

public interface IPythonLocator
{
    Task<(string? PythonPath, string? VersionLine, bool IsValidPython311)> LocatePythonAsync(
        string? explicitPath = null,
        CancellationToken ct = default);
}
