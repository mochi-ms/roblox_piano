namespace RobloxPiano.Core.Transcription;

public record TranscriptionRequest(
    string JobId,
    string NormalizedAudioPath,
    string? OutputWorkspaceRoot = null,
    string? SourceTitle = null,
    TranscriptionOptions? Options = null
)
{
    public TranscriptionOptions EffectiveOptions => Options ?? TranscriptionOptions.Default;
}
