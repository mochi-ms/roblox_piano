namespace RobloxPiano.Core.YouTube;

public record YouTubeDownloadProgress(
    string Phase,
    double? Percent,
    string Message,
    long? DownloadedBytes = null,
    long? TotalBytes = null,
    double? SpeedBytesPerSec = null,
    double? EtaSeconds = null
);
