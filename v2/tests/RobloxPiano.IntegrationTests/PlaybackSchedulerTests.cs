using System.Diagnostics;
using RobloxPiano.Core.Music;
using RobloxPiano.Core.Piano;
using RobloxPiano.Playback.Windows.Input;
using RobloxPiano.Playback.Windows.Playback;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class PlaybackSchedulerTests
{
    [Fact]
    public async Task Playback_NormalCompletion_FiresEventsAndReleasesAll()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 5);
        using var scheduler = new PlaybackScheduler(engine, keyState);

        scheduler.CountdownSeconds = 0;
        scheduler.Speed = 10.0; // 10x fast speed for testing

        var timeline = new MusicTimeline("Test Short");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.05));
        timeline.AddNote(new NoteEvent(64, 0.05, 0.10));

        scheduler.SetTimeline(timeline);
        scheduler.Play();

        // Wait for completion (should take < 0.2s at 10x speed)
        for (int i = 0; i < 20; i++)
        {
            if (scheduler.State == PlaybackState.Completed) break;
            await Task.Delay(20);
        }

        Assert.Equal(PlaybackState.Completed, scheduler.State);
        Assert.NotEmpty(backend.Events);
        Assert.Empty(keyState.ActiveKeys);
        Assert.Empty(keyState.ActiveModifiers);
    }

    [Fact]
    public async Task Playback_StopAndReset_ReleasesKeysAndSetsStopped()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;

        var timeline = new MusicTimeline("Long Song");
        timeline.AddNote(new NoteEvent(60, 0.0, 1.0));
        timeline.AddNote(new NoteEvent(64, 5.0, 6.0));

        scheduler.SetTimeline(timeline);
        scheduler.Play();

        await Task.Delay(50);
        scheduler.Stop();

        Assert.Equal(PlaybackState.Stopped, scheduler.State);
        Assert.Empty(keyState.ActiveKeys);
        Assert.Empty(keyState.ActiveModifiers);
    }

    [Fact]
    public async Task Playback_Countdown_CanBeStoppedBeforeFirstNote()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 3;

        var timeline = new MusicTimeline("Countdown Song");
        timeline.AddNote(new NoteEvent(60, 0.0, 1.0));

        scheduler.SetTimeline(timeline);
        scheduler.Play();

        Assert.Equal(PlaybackState.Countdown, scheduler.State);

        await Task.Delay(50);
        scheduler.Stop();

        Assert.Equal(PlaybackState.Stopped, scheduler.State);
        Assert.Empty(backend.Events); // No keystrokes emitted!
        Assert.Empty(keyState.ActiveKeys);
    }

    [Fact]
    public async Task Playback_PauseAndResume_DoesNotAdvanceSongTimeWhilePaused()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 5);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;
        scheduler.Speed = 5.0;

        var timeline = new MusicTimeline("Pause Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.1));
        timeline.AddNote(new NoteEvent(64, 0.3, 0.4));
        timeline.AddNote(new NoteEvent(67, 0.6, 0.7));

        scheduler.SetTimeline(timeline);
        scheduler.Play();

        await Task.Delay(30);
        scheduler.Pause();

        Assert.Equal(PlaybackState.Paused, scheduler.State);
        Assert.Empty(keyState.ActiveKeys);

        // Wait while paused
        await Task.Delay(150);

        scheduler.Resume();
        Assert.Equal(PlaybackState.Playing, scheduler.State);

        // Wait for completion
        for (int i = 0; i < 30; i++)
        {
            if (scheduler.State == PlaybackState.Completed) break;
            await Task.Delay(20);
        }

        Assert.Equal(PlaybackState.Completed, scheduler.State);
        Assert.Empty(keyState.ActiveKeys);
    }

    [Fact]
    public async Task Playback_Seek_MovesPositionAndClearsPreviousEvents()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 5);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;
        scheduler.Speed = 10.0;

        var timeline = new MusicTimeline("Seek Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.1)); // Pitch 60 (C4 -> 't')
        timeline.AddNote(new NoteEvent(72, 1.0, 1.1)); // Pitch 72 (C5 -> 's')

        scheduler.SetTimeline(timeline);
        scheduler.Seek(0.5); // Seek past the first note

        scheduler.Play(0.5);

        for (int i = 0; i < 20; i++)
        {
            if (scheduler.State == PlaybackState.Completed) break;
            await Task.Delay(20);
        }

        Assert.Equal(PlaybackState.Completed, scheduler.State);

        var keys = backend.Events.Select(e => e.Key).ToList();
        Assert.DoesNotContain("t", keys); // Pitch 60 was skipped!
        Assert.Contains("s", keys);        // Pitch 72 was played
    }

    [Fact]
    public async Task Playback_Transpose_AppliesSemitonesToMappingWithoutMutatingTimeline()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 5);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;
        scheduler.Speed = 10.0;

        var timeline = new MusicTimeline("Transpose Test");
        var n = new NoteEvent(60, 0.0, 0.1);
        timeline.AddNote(n);

        scheduler.SetTimeline(timeline);
        scheduler.Transpose = 12; // Transpose +12 -> plays C5 ('s')

        scheduler.Play();

        for (int i = 0; i < 20; i++)
        {
            if (scheduler.State == PlaybackState.Completed) break;
            await Task.Delay(20);
        }

        Assert.Equal(PlaybackState.Completed, scheduler.State);
        Assert.Equal(60, n.Pitch); // Source timeline untouched!

        var keys = backend.Events.Select(e => e.Key).ToList();
        Assert.Contains("s", keys); // Transposed pitch played
    }

    [Fact]
    public async Task Playback_Filtering_RH_LH_FiltersAccurately()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 5);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;
        scheduler.Speed = 10.0;
        scheduler.EnableRH = false; // Disable Right Hand

        var timeline = new MusicTimeline("Filter Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.1, hand: HandType.Right)); // RH C4 ('t')
        timeline.AddNote(new NoteEvent(48, 0.0, 0.1, hand: HandType.Left));  // LH C3 ('8')

        scheduler.SetTimeline(timeline);
        scheduler.Play();

        for (int i = 0; i < 20; i++)
        {
            if (scheduler.State == PlaybackState.Completed) break;
            await Task.Delay(20);
        }

        Assert.Equal(PlaybackState.Completed, scheduler.State);

        var keys = backend.Events.Select(e => e.Key).ToList();
        Assert.DoesNotContain("t", keys); // RH skipped
        Assert.Contains("8", keys);        // LH played
    }

    [Fact]
    public async Task Playback_Exception_ReleasesAllAndSetsErrorState()
    {
        var failingBackend = new FailingPlaybackBackend(failOnKeyDownCount: 2);
        using var keyState = new KeyStateManager(failingBackend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 5);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;
        scheduler.Speed = 10.0;

        var timeline = new MusicTimeline("Fail Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.1));
        timeline.AddNote(new NoteEvent(64, 0.05, 0.1));

        bool errorFired = false;
        scheduler.PlaybackError += (_, _) => errorFired = true;

        scheduler.SetTimeline(timeline);
        scheduler.Play();

        for (int i = 0; i < 30; i++)
        {
            if (errorFired) break;
            await Task.Delay(20);
        }

        Assert.True(errorFired, "PlaybackError event must fire on exception");
        Assert.Equal(PlaybackState.Stopped, scheduler.State);
        Assert.Empty(keyState.ActiveKeys);
        Assert.Empty(keyState.ActiveModifiers);
    }

    [Fact]
    public async Task Playback_StuckShift_RecoveryTest()
    {
        var failingBackend = new FailingPlaybackBackend(failOnKeyChar: "w");
        using var keyState = new KeyStateManager(failingBackend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 5);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;

        var timeline = new MusicTimeline("Shift Fail");
        // Note: G#3 (56) -> Shift + w (fails on 'w')
        timeline.AddNote(new NoteEvent(56, 0.0, 0.1));

        scheduler.SetTimeline(timeline);
        scheduler.Play();

        await Task.Delay(100);

        // Verification: No active SHIFT modifier may remain held
        Assert.Empty(keyState.ActiveModifiers);
        Assert.Empty(keyState.ActiveKeys);
    }

    [Fact]
    public async Task Playback_RapidPlayStop_NoDeadlocksOrOverlappingLoops()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 5);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;

        var timeline = new MusicTimeline("Rapid Test");
        for (int i = 0; i < 50; i++)
        {
            timeline.AddNote(new NoteEvent(60 + (i % 12), i * 0.05, (i + 1) * 0.05));
        }

        scheduler.SetTimeline(timeline);

        for (int cycle = 0; cycle < 15; cycle++)
        {
            scheduler.Play();
            await Task.Delay(10);
            scheduler.Stop();
            await Task.Delay(5);
        }

        Assert.Equal(PlaybackState.Stopped, scheduler.State);
        Assert.Empty(keyState.ActiveKeys);
        Assert.Empty(keyState.ActiveModifiers);
    }

    [Fact]
    public async Task TimingAccuracy_MonotonicTargetScheduling_NoCumulativeDrift()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 2);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;
        scheduler.Speed = 2.0; // 2x speed -> 20 events * 0.02s = 0.4s song time -> 0.2s wall time

        var timeline = new MusicTimeline("Timing Test");
        for (int i = 0; i < 20; i++)
        {
            timeline.AddNote(new NoteEvent(60, i * 0.02, (i + 1) * 0.02));
        }

        scheduler.SetTimeline(timeline);

        long start = Stopwatch.GetTimestamp();
        scheduler.Play();

        for (int i = 0; i < 30; i++)
        {
            if (scheduler.State == PlaybackState.Completed) break;
            await Task.Delay(20);
        }

        long elapsedTicks = Stopwatch.GetTimestamp() - start;
        double elapsedSeconds = (double)elapsedTicks / Stopwatch.Frequency;

        Assert.Equal(PlaybackState.Completed, scheduler.State);
        // Expected wall duration ~0.20s (allow reasonable CI scheduling buffer: 0.15s - 0.40s)
        Assert.True(elapsedSeconds >= 0.15 && elapsedSeconds <= 0.45, $"Measured duration was {elapsedSeconds:F3}s");
    }

    private class FailingPlaybackBackend : IPlaybackBackend
    {
        private readonly int _failOnKeyDownCount;
        private readonly string? _failOnKeyChar;
        private int _keyDownCount;

        public FailingPlaybackBackend(int failOnKeyDownCount = -1, string? failOnKeyChar = null)
        {
            _failOnKeyDownCount = failOnKeyDownCount;
            _failOnKeyChar = failOnKeyChar;
        }

        public void KeyDown(string key)
        {
            _keyDownCount++;
            if (_keyDownCount == _failOnKeyDownCount || string.Equals(key, _failOnKeyChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Simulated backend failure on KeyDown({key})");
            }
        }

        public void KeyUp(string key) { }
        public void ReleaseAll() { }
        public void Dispose() { }
    }
}
