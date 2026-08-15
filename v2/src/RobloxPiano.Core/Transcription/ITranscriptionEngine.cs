namespace RobloxPiano.Core.Transcription;

public interface ITranscriptionEngine : IDisposable
{
    Task<TranscriptionResult> TranscribeAsync(
        TranscriptionRequest request,
        IProgress<TranscriptionProgress>? progress = null,
        CancellationToken ct = default);

    Task<TranscriptionEngineStatus> CheckAvailabilityAsync(CancellationToken ct = default);
}
