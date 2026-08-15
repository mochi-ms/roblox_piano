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
    public async Task Playback_DisposeWhilePlaying_WorkerTerminatesAndKeysReleased()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 5);
        var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;

        var timeline = new MusicTimeline("Dispose Test");
        for (int i = 0; i < 20; i++)
        {
            timeline.AddNote(new NoteEvent(60, i * 0.1, (i + 1) * 0.1));
        }

        scheduler.SetTimeline(timeline);
        scheduler.Play();

        await Task.Delay(50);

        scheduler.Dispose();

        int eventCountAtDispose = backend.Events.Count;
        await Task.Delay(100);

        Assert.Equal(eventCountAtDispose, backend.Events.Count);
        Assert.Empty(keyState.ActiveKeys);
        Assert.Empty(keyState.ActiveModifiers);
    }

    [Fact]
    public async Task Playback_Stop_ReturnsWithNoWorkerStillEmitting()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 5);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;

        var timeline = new MusicTimeline("Stop Test");
        for (int i = 0; i < 30; i++)
        {
            timeline.AddNote(new NoteEvent(60, i * 0.05, (i + 1) * 0.05));
        }

        scheduler.SetTimeline(timeline);
        scheduler.Play();

        await Task.Delay(50);
        scheduler.Stop();

        int eventCountAtStop = backend.Events.Count;
        await Task.Delay(100);

        Assert.Equal(eventCountAtStop, backend.Events.Count);
        Assert.Empty(keyState.ActiveKeys);
    }

    [Fact]
    public async Task Playback_PausedSeek_ResumeStartsFromNewPosition()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 5);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;
        scheduler.Speed = 5.0;

        // Timeline: 0.0 -> pitch 53 ('q'), 1.0 -> pitch 55 ('w'), 2.0 -> pitch 57 ('e'), 3.0 -> pitch 59 ('r')
        var timeline = new MusicTimeline("Paused Seek Test");
        timeline.AddNote(new NoteEvent(53, 0.0, 0.1));
        timeline.AddNote(new NoteEvent(55, 0.2, 0.3));
        timeline.AddNote(new NoteEvent(57, 1.0, 1.1));
        timeline.AddNote(new NoteEvent(59, 1.2, 1.3));

        scheduler.SetTimeline(timeline);
        scheduler.Play();

        await Task.Delay(30);
        scheduler.Pause();
        Assert.Equal(PlaybackState.Paused, scheduler.State);

        // Seek while paused to 1.0s (past 'q' and 'w')
        scheduler.Seek(1.0);
        Assert.Equal(PlaybackState.Paused, scheduler.State);
        Assert.True(Math.Abs(scheduler.CurrentTime - 1.0) < 0.01);

        int eventsBeforeResume = backend.Events.Count;
        await Task.Delay(100);
        Assert.Equal(eventsBeforeResume, backend.Events.Count); // No notes while paused

        scheduler.Resume();
        Assert.Equal(PlaybackState.Playing, scheduler.State);

        for (int i = 0; i < 30; i++)
        {
            if (scheduler.State == PlaybackState.Completed) break;
            await Task.Delay(20);
        }

        Assert.Equal(PlaybackState.Completed, scheduler.State);

        // After resume from 1.0s, only notes at 1.0s+ ('e' and 'r') should be emitted
        var resumedKeys = backend.Events.Skip(eventsBeforeResume).Select(e => e.Key).ToList();
        Assert.Contains("e", resumedKeys);
        Assert.Contains("r", resumedKeys);
    }

    [Fact]
    public async Task Playback_SpeedChangeDuringLongGap_ReanchorsImmediately()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 5);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;
        scheduler.Speed = 1.0;

        // Note A at 0.0s, Note B at 2.0s
        var timeline = new MusicTimeline("Speed Gap Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.1)); // 't'
        timeline.AddNote(new NoteEvent(72, 2.0, 2.1)); // 's'

        scheduler.SetTimeline(timeline);

        long start = Stopwatch.GetTimestamp();
        scheduler.Play();

        // Wait 0.2s wall time, then increase speed to 4.0x
        await Task.Delay(200);
        scheduler.Speed = 4.0;

        for (int i = 0; i < 40; i++)
        {
            if (scheduler.State == PlaybackState.Completed) break;
            await Task.Delay(30);
        }

        long elapsedTicks = Stopwatch.GetTimestamp() - start;
        double elapsedSeconds = (double)elapsedTicks / Stopwatch.Frequency;

        Assert.Equal(PlaybackState.Completed, scheduler.State);
        // At 1.0x for 0.2s + (1.8s / 4.0 = 0.45s) -> Expected wall time ~0.65s (much less than 2.0s!)
        Assert.True(elapsedSeconds < 1.3, $"Elapsed wall time was {elapsedSeconds:F3}s, expected < 1.3s");

        var keys = backend.Events.Select(e => e.Key).ToList();
        Assert.Contains("t", keys);
        Assert.Contains("s", keys);
    }

    [Fact]
    public async Task Playback_ProgressAdvancesDuringLongGap()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 5);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;
        scheduler.Speed = 1.0;

        // Note at 0.0s, Note at 3.0s
        var timeline = new MusicTimeline("Progress Gap Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.1));
        timeline.AddNote(new NoteEvent(72, 3.0, 3.1));

        scheduler.SetTimeline(timeline);
        scheduler.Play();

        // Wait 0.5s in the middle of gap
        await Task.Delay(500);

        double progressInGap = scheduler.CurrentTime;
        scheduler.Stop();

        // Progress must advance continuously past 0.3s during the 3.0s rest
        Assert.True(progressInGap >= 0.3, $"Progress was {progressInGap:F3}s, expected >= 0.3s");
    }

    [Fact]
    public async Task Playback_ProgressDoesNotAdvanceWhilePaused()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 5);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;

        var timeline = new MusicTimeline("Paused Progress Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.1));
        timeline.AddNote(new NoteEvent(72, 2.0, 2.1));

        scheduler.SetTimeline(timeline);
        scheduler.Play();

        await Task.Delay(100);
        scheduler.Pause();

        double pausedTime1 = scheduler.CurrentTime;
        await Task.Delay(200);
        double pausedTime2 = scheduler.CurrentTime;

        scheduler.Stop();

        Assert.Equal(pausedTime1, pausedTime2);
    }

    [Fact]
    public void Playback_Diagnostics_CountActualMappedAndSkippedNotes()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 5);

        // One valid pitch (60 -> C4), One unmappable pitch (10)
        var n1 = new NoteEvent(60, 0.0, 0.1);
        var n2 = new NoteEvent(10, 0.0, 0.1);

        var result = engine.PlayChordNotes(new[] { n1, n2 });

        Assert.Equal(2, result.RequestedCount);
        Assert.Equal(1, result.PlayedCount);
        Assert.Equal(1, result.SkippedUnmappedCount);
        Assert.Equal(0, result.SkippedConflictCount);
    }

    [Fact]
    public async Task Playback_Timing_05x()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 2);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;
        scheduler.Speed = 0.5; // 0.5x speed -> 0.10s song time = 0.20s wall time

        var timeline = new MusicTimeline("Timing 0.5x");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.05));
        timeline.AddNote(new NoteEvent(64, 0.10, 0.15));

        scheduler.SetTimeline(timeline);

        long start = Stopwatch.GetTimestamp();
        scheduler.Play();

        for (int i = 0; i < 30; i++)
        {
            if (scheduler.State == PlaybackState.Completed) break;
            await Task.Delay(20);
        }

        double elapsedSeconds = (double)(Stopwatch.GetTimestamp() - start) / Stopwatch.Frequency;

        Assert.Equal(PlaybackState.Completed, scheduler.State);
        // Expected ~0.20s (tolerances: 0.15s - 0.40s)
        Assert.True(elapsedSeconds >= 0.15 && elapsedSeconds <= 0.45, $"0.5x measured duration was {elapsedSeconds:F3}s");
    }

    [Fact]
    public async Task Playback_Timing_10x()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 2);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;
        scheduler.Speed = 1.0; // 1.0x speed -> 0.20s song time = 0.20s wall time

        var timeline = new MusicTimeline("Timing 1.0x");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.1));
        timeline.AddNote(new NoteEvent(64, 0.20, 0.25));

        scheduler.SetTimeline(timeline);

        long start = Stopwatch.GetTimestamp();
        scheduler.Play();

        for (int i = 0; i < 30; i++)
        {
            if (scheduler.State == PlaybackState.Completed) break;
            await Task.Delay(20);
        }

        double elapsedSeconds = (double)(Stopwatch.GetTimestamp() - start) / Stopwatch.Frequency;

        Assert.Equal(PlaybackState.Completed, scheduler.State);
        // Expected ~0.20s (tolerances: 0.15s - 0.40s)
        Assert.True(elapsedSeconds >= 0.15 && elapsedSeconds <= 0.45, $"1.0x measured duration was {elapsedSeconds:F3}s");
    }

    [Fact]
    public async Task Playback_Timing_20x()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 2);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;
        scheduler.Speed = 2.0; // 2.0x speed -> 0.40s song time = 0.20s wall time

        var timeline = new MusicTimeline("Timing 2.0x");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.1));
        timeline.AddNote(new NoteEvent(64, 0.40, 0.45));

        scheduler.SetTimeline(timeline);

        long start = Stopwatch.GetTimestamp();
        scheduler.Play();

        for (int i = 0; i < 30; i++)
        {
            if (scheduler.State == PlaybackState.Completed) break;
            await Task.Delay(20);
        }

        double elapsedSeconds = (double)(Stopwatch.GetTimestamp() - start) / Stopwatch.Frequency;

        Assert.Equal(PlaybackState.Completed, scheduler.State);
        // Expected ~0.20s (tolerances: 0.15s - 0.40s)
        Assert.True(elapsedSeconds >= 0.15 && elapsedSeconds <= 0.45, $"2.0x measured duration was {elapsedSeconds:F3}s");
    }

    [Fact]
    public async Task Playback_LongTimeline_DriftRemainsBounded()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 1);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;
        scheduler.Speed = 5.0; // 5x speed -> 100 events * 0.02s = 2.0s song time -> 0.40s wall time

        var timeline = new MusicTimeline("100 Events Drift Test");
        for (int i = 0; i < 100; i++)
        {
            timeline.AddNote(new NoteEvent(60, i * 0.02, (i + 1) * 0.02));
        }

        scheduler.SetTimeline(timeline);

        long start = Stopwatch.GetTimestamp();
        scheduler.Play();

        for (int i = 0; i < 50; i++)
        {
            if (scheduler.State == PlaybackState.Completed) break;
            await Task.Delay(20);
        }

        double elapsedSeconds = (double)(Stopwatch.GetTimestamp() - start) / Stopwatch.Frequency;

        Assert.Equal(PlaybackState.Completed, scheduler.State);
        // Expected ~0.40s wall time, bounded drift within 0.35s - 0.65s
        Assert.True(elapsedSeconds >= 0.30 && elapsedSeconds <= 0.70, $"100 events measured duration was {elapsedSeconds:F3}s");
    }

    [Fact]
    public async Task LoadNewScoreWhilePlaying_OldWorkerCannotEmitAfterReplacement()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 5);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;

        var oldTimeline = new MusicTimeline("Old Song");
        for (int i = 0; i < 50; i++)
        {
            oldTimeline.AddNote(new NoteEvent(60, i * 0.05, (i + 1) * 0.05));
        }

        scheduler.SetTimeline(oldTimeline);
        scheduler.Play();

        await Task.Delay(50);

        var newTimeline = new MusicTimeline("New Song");
        newTimeline.AddNote(new NoteEvent(72, 0.0, 0.1));
        scheduler.SetTimeline(newTimeline);

        Assert.Equal(PlaybackState.Idle, scheduler.State);
        int eventCountAtReplacement = backend.Events.Count;

        await Task.Delay(100);

        Assert.Equal(eventCountAtReplacement, backend.Events.Count);
        Assert.Empty(keyState.ActiveKeys);
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
