using RobloxPiano.Core.Music;

namespace RobloxPiano.Core.Transcription;

public record TranscriptionResult(
    bool Success,
    string JobId,
    string SourceAudioPath,
    string? GeneratedMidiPath = null,
    MusicTimeline? Timeline = null,
    TimeSpan Duration = default,
    int NoteCount = 0,
    int PlayableNoteCount = 0,
    int OutOfRangeNoteCount = 0,
    int? MinPitch = null,
    int? MaxPitch = null,
    string EngineName = "Basic Pitch",
    string EngineVersion = "0.4.0",
    double RuntimeSeconds = 0.0,
    string? ErrorCode = null,
    string? ErrorMessage = null
)
{
    public static TranscriptionResult Successful(
        string jobId,
        string sourceAudioPath,
        string generatedMidiPath,
        MusicTimeline timeline,
        int playableNoteCount,
        int outOfRangeNoteCount,
        int? minPitch,
        int? maxPitch,
        double runtimeSeconds = 0.0,
        string engineName = "Basic Pitch",
        string engineVersion = "0.4.0") =>
        new(
            Success: true,
            JobId: jobId,
            SourceAudioPath: sourceAudioPath,
            GeneratedMidiPath: generatedMidiPath,
            Timeline: timeline,
            Duration: TimeSpan.FromSeconds(timeline.Duration),
            NoteCount: timeline.Notes.Count,
            PlayableNoteCount: playableNoteCount,
            OutOfRangeNoteCount: outOfRangeNoteCount,
            MinPitch: minPitch,
            MaxPitch: maxPitch,
            EngineName: engineName,
            EngineVersion: engineVersion,
            RuntimeSeconds: runtimeSeconds
        );

    public static TranscriptionResult Failed(
        string jobId,
        string sourceAudioPath,
        string errorMessage,
        string errorCode = "TRANSCRIPTION_FAILED",
        double runtimeSeconds = 0.0,
        string engineName = "Basic Pitch",
        string engineVersion = "0.4.0") =>
        new(
            Success: false,
            JobId: jobId,
            SourceAudioPath: sourceAudioPath,
            ErrorCode: errorCode,
            ErrorMessage: errorMessage,
            RuntimeSeconds: runtimeSeconds,
            EngineName: engineName,
            EngineVersion: engineVersion
        );
}
