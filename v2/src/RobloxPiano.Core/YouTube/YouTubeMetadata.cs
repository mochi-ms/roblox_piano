namespace RobloxPiano.Core.YouTube;

public record YouTubeMetadata(
    string Id,
    string Title,
    double DurationSeconds,
    string Channel,
    string WebpageUrl,
    string? ThumbnailUrl = null,
    bool IsLive = false,
    string? LiveStatus = null,
    string? Extractor = null
);
