namespace RobloxPiano.Core.Audio;

public interface IAudioIngestionService
{
    Task<AudioIngestResult> IngestAudioAsync(
        AudioIngestRequest request,
        IProgress<double>? progress = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<AudioIngestResult>> IngestBatchAsync(
        IReadOnlyList<AudioIngestRequest> requests,
        IProgress<(int Current, int Total, string FileName, double Progress)>? progress = null,
        CancellationToken ct = default);
}
