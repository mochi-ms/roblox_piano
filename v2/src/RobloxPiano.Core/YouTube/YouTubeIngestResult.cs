using RobloxPiano.Core.Audio;

namespace RobloxPiano.Core.YouTube;

public record YouTubeIngestResult(
    bool Success,
    string JobId,
    string? VideoId,
    string? OriginalUrl,
    string? CanonicalUrl,
    string? Title,
    string? ChannelName,
    double DurationSeconds,
    string? ThumbnailUrl,
    string? NormalizedAudioPath,
    AudioIngestResult? AudioIngestResult,
    string? ErrorCode,
    string? ErrorMessage
)
{
    public static YouTubeIngestResult Successful(
        string jobId,
        string videoId,
        string originalUrl,
        string canonicalUrl,
        string title,
        string channelName,
        double durationSeconds,
        string? thumbnailUrl,
        string normalizedAudioPath,
        AudioIngestResult audioIngestResult) =>
        new(true, jobId, videoId, originalUrl, canonicalUrl, title, channelName, durationSeconds, thumbnailUrl, normalizedAudioPath, audioIngestResult, null, null);

    public static YouTubeIngestResult Failed(
        string jobId,
        string errorMessage,
        string? errorCode = "YOUTUBE_INGEST_ERROR",
        string? videoId = null,
        string? originalUrl = null,
        string? canonicalUrl = null,
        string? title = null,
        string? channelName = null,
        double durationSeconds = 0,
        string? thumbnailUrl = null) =>
        new(false, jobId, videoId, originalUrl, canonicalUrl, title, channelName, durationSeconds, thumbnailUrl, null, null, errorCode, errorMessage);

    public static YouTubeIngestResult Cancelled(
        string jobId,
        string? videoId = null,
        string? originalUrl = null,
        string? canonicalUrl = null) =>
        new(false, jobId, videoId, originalUrl, canonicalUrl, null, null, 0, null, null, null, "CANCELLED", YouTubeError.Cancelled);
}
