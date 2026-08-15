namespace RobloxPiano.Core.YouTube;

public interface IYouTubeIngestionService
{
    Task<YouTubeToolStatus> CheckToolStatusAsync(CancellationToken ct = default);
    Task<YouTubeMetadata> ProbeMetadataAsync(string url, CancellationToken ct = default);
    Task<YouTubeIngestResult> IngestYouTubeAsync(
        YouTubeIngestRequest request,
        IProgress<YouTubeDownloadProgress>? progress = null,
        CancellationToken ct = default);
}
