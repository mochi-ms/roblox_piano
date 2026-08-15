namespace RobloxPiano.Core.Audio;

public class AudioIngestResult
{
    public string JobId { get; }
    public string SourcePath { get; }
    public string? NormalizedAudioPath { get; }
    public AudioMetadata? Metadata { get; }
    public bool Success { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }

    private AudioIngestResult(
        string jobId,
        string sourcePath,
        string? normalizedAudioPath,
        AudioMetadata? metadata,
        bool success,
        string? errorCode,
        string? errorMessage)
    {
        JobId = jobId;
        SourcePath = sourcePath;
        NormalizedAudioPath = normalizedAudioPath;
        Metadata = metadata;
        Success = success;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public static AudioIngestResult Successful(
        string jobId,
        string sourcePath,
        string normalizedAudioPath,
        AudioMetadata metadata) =>
        new(jobId, sourcePath, normalizedAudioPath, metadata, true, null, null);

    public static AudioIngestResult Failed(
        string sourcePath,
        string errorMessage,
        string? errorCode = "AUDIO_INGEST_ERROR",
        string? jobId = null,
        AudioMetadata? metadata = null) =>
        new(jobId ?? Guid.NewGuid().ToString("N"), sourcePath, null, metadata, false, errorCode, errorMessage);
}
