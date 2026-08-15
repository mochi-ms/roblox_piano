using RobloxPiano.Core.Music;
using RobloxPiano.Core.Piano;
using RobloxPiano.Playback.Windows.Input;
using RobloxPiano.Playback.Windows.Playback;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class ChordEngineTests
{
    [Fact]
    public void MixedChord_ShiftSeparation_IsolatesUnshiftedFromShiftedNotes()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 10, modifierSettleMs: 2);

        // Note 1: F3 (53) -> 'q' (unshifted)
        // Note 2: G#3 (56) -> 'W' (Shift + w)
        var n1 = new NoteEvent(53, 0.0, 0.1);
        var n2 = new NoteEvent(56, 0.0, 0.1);

        engine.PlayChordNotes(new[] { n1, n2 });

        var events = backend.Events;
        var actions = events.Select(e => (e.Action, e.Key)).ToList();

        Assert.Contains((BackendAction.KeyDown, "q"), actions);
        Assert.Contains((BackendAction.KeyDown, "shift"), actions);
        Assert.Contains((BackendAction.KeyDown, "w"), actions);

        // Verify 'q' down and 'q' up happens BEFORE 'shift' down
        int qDownIdx = actions.IndexOf((BackendAction.KeyDown, "q"));
        int qUpIdx = actions.IndexOf((BackendAction.KeyUp, "q"));
        int shiftDownIdx = actions.IndexOf((BackendAction.KeyDown, "shift"));

        Assert.True(qDownIdx < qUpIdx, "q down must precede q up");
        Assert.True(qUpIdx < shiftDownIdx, "q up must precede shift down to isolate modifiers");

        // Verify 'w' up happens before 'shift' up
        int wUpIdx = actions.IndexOf((BackendAction.KeyUp, "w"));
        int shiftUpIdx = actions.IndexOf((BackendAction.KeyUp, "shift"));
        Assert.True(wUpIdx < shiftUpIdx, "w up must precede shift up");

        // Verify all keys and modifiers are fully released
        Assert.Empty(keyState.ActiveKeys);
        Assert.Empty(keyState.ActiveModifiers);
    }

    [Fact]
    public void SamePhysicalKeyConflict_UsesMicroArpeggio()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(
            keyState,
            mapper,
            conflictPolicy: ConflictPolicy.MicroArpeggio,
            conflictDelayMs: 5,
            defaultHoldDurationMs: 10
        );

        // Note 1: F3 (53) -> 'q' (unshifted)
        // Note 2: F#3 (54) -> 'Q' (Shift + q) - SAME PHYSICAL KEY 'q'!
        var n1 = new NoteEvent(53, 0.0, 0.1);
        var n2 = new NoteEvent(54, 0.0, 0.1);

        engine.PlayChordNotes(new[] { n1, n2 });

        var events = backend.Events;
        var actions = events.Select(e => (e.Action, e.Key)).ToList();

        // Verify 'q' is pressed, then released BEFORE 'shift' is pressed for 'Q'
        Assert.Contains((BackendAction.KeyDown, "q"), actions);
        int firstQDown = actions.IndexOf((BackendAction.KeyDown, "q"));
        int firstQUp = actions.IndexOf((BackendAction.KeyUp, "q"));
        int shiftDown = actions.IndexOf((BackendAction.KeyDown, "shift"));

        Assert.True(firstQDown < firstQUp && firstQUp < shiftDown, "First 'q' must complete before shifted 'q'");
        Assert.Empty(keyState.ActiveKeys);
        Assert.Empty(keyState.ActiveModifiers);
    }

    [Fact]
    public void SkipConflictedPolicy_KeepsOnlyHighestPitch()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(
            keyState,
            mapper,
            conflictPolicy: ConflictPolicy.SkipConflicted,
            defaultHoldDurationMs: 10
        );

        // Note 1: F3 (53) -> 'q' (unshifted)
        // Note 2: F#3 (54) -> 'Q' (Shift + q)
        var n1 = new NoteEvent(53, 0.0, 0.1);
        var n2 = new NoteEvent(54, 0.0, 0.1);

        engine.PlayChordNotes(new[] { n1, n2 });

        var events = backend.Events;
        var actions = events.Select(e => (e.Action, e.Key)).ToList();

        // Only pitch 54 (Shift + q) should be played
        Assert.Contains((BackendAction.KeyDown, "shift"), actions);
        Assert.Contains((BackendAction.KeyDown, "q"), actions);
        Assert.Equal(4, actions.Count); // Shift down, q down, q up, Shift up
    }

    [Fact]
    public void Transpose_ShiftsMappedPitchesAccurately()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 10);

        // Note: C4 (60) mapped with transpose +12 -> C5 (72) -> 's' (unshifted)
        var n = new NoteEvent(60, 0.0, 0.1);
        engine.PlayChordNotes(new[] { n }, transpose: 12);

        var actions = backend.Events.Select(e => (e.Action, e.Key)).ToList();
        Assert.Contains((BackendAction.KeyDown, "s"), actions);
        Assert.Contains((BackendAction.KeyUp, "s"), actions);
    }

    [Fact]
    public void UnmappablePitch_SkippedWithoutCrashing()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 10);

        // Out of range pitch (e.g. 10)
        var n = new NoteEvent(10, 0.0, 0.1);
        engine.PlayChordNotes(new[] { n });

        Assert.Empty(backend.Events);
        Assert.Empty(keyState.ActiveKeys);
    }
}
