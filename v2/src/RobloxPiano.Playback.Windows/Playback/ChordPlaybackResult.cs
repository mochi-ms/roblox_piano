namespace RobloxPiano.Playback.Windows.Playback;

public record ChordPlaybackResult(
    int RequestedCount,
    int PlayedCount,
    int SkippedUnmappedCount,
    int SkippedConflictCount,
    IReadOnlyList<int> PlayedPitches
);
