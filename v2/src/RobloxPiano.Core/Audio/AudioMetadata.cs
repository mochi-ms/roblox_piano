namespace RobloxPiano.Core.Audio;

public record AudioMetadata(
    string SourcePath,
    string ContainerFormat,
    string CodecName,
    double DurationSeconds,
    int SampleRate,
    int Channels,
    long? BitRate,
    long FileSizeBytes,
    int AudioStreamCount,
    string? Title = null,
    string? Artist = null
);
