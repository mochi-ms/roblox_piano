namespace RobloxPiano.Core.Transcription;

public record TranscriptionEngineStatus(
    bool IsAvailable,
    string? PythonPath = null,
    string? PythonVersion = null,
    string? BasicPitchVersion = null,
    string StatusMessage = ""
)
{
    public static TranscriptionEngineStatus Available(string pythonPath, string pythonVersion, string basicPitchVersion) =>
        new(true, pythonPath, pythonVersion, basicPitchVersion, $"● Basic Pitch AI 준비됨 (v{basicPitchVersion} / Python {pythonVersion})");

    public static TranscriptionEngineStatus Unavailable(string reason) =>
        new(false, StatusMessage: $"▲ AI 엔진 미설치: {reason}");
}
