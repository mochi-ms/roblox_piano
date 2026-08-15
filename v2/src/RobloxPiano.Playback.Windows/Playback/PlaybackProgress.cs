namespace RobloxPiano.Playback.Windows.Playback;

public record PlaybackProgress(
    double CurrentTime,
    double TotalTime,
    double ProgressRatio,
    int PlayedNotes,
    int SkippedNotes
);
