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
        scheduler.Speed = 10.0; // Fast execution for unit test

        var timeline = new MusicTimeline("Simple Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.1)); // C4 -> 't'
        timeline.AddNote(new NoteEvent(64, 0.1, 0.2)); // E4 -> 'u'
        timeline.AddPedal(new PedalEvent(0.0, true));
        timeline.AddPedal(new PedalEvent(0.2, false));

        scheduler.SetTimeline(timeline);

        var states = new List<PlaybackState>();
        scheduler.StateChanged += (_, s) => states.Add(s);

        int countdownTicks = 0;
        scheduler.CountdownTick += (_, _) => countdownTicks++;

        scheduler.Play();

        // Await playback completion
        for (int i = 0; i < 30; i++)
        {
            if (scheduler.State == PlaybackState.Completed) break;
            await Task.Delay(20);
        }

        Assert.Equal(PlaybackState.Completed, scheduler.State);
        Assert.Contains(PlaybackState.Playing, states);
        Assert.Contains(PlaybackState.Completed, states);
        Assert.Equal(0, countdownTicks); // Countdown was disabled

        // Verify keys were released
        Assert.Empty(keyState.ActiveKeys);
        Assert.Empty(keyState.ActiveModifiers);

        var keys = backend.Events.Select(e => e.Key).ToList();
        Assert.Contains("t", keys);
        Assert.Contains("u", keys);
    }

    [Fact]
    public async Task Playback_PauseResume_PreservesTimelinePositionAndState()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 5);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;
        scheduler.Speed = 1.0;

        var timeline = new MusicTimeline("Pause Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.1));
        timeline.AddNote(new NoteEvent(64, 0.5, 0.6));

        scheduler.SetTimeline(timeline);
        scheduler.Play();

        await Task.Delay(50);
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
        Assert.False(scheduler.HasActiveWorker);
        Assert.Empty(keyState.ActiveKeys);
        Assert.Empty(keyState.ActiveModifiers);
    }

    [Fact]
    public async Task Playback_Countdown_CanBeStoppedBeforeFirstNote()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 5);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 3;

        var timeline = new MusicTimeline("Countdown Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.1));

        scheduler.SetTimeline(timeline);
        scheduler.Play();

        await Task.Delay(200);
        Assert.Equal(PlaybackState.Countdown, scheduler.State);

        scheduler.Stop();
        Assert.Equal(PlaybackState.Stopped, scheduler.State);
        Assert.False(scheduler.HasActiveWorker);
        Assert.Empty(backend.Events);
    }

    [Fact]
    public async Task Playback_HasActiveWorker_TracksTerminatingWorkerAccurately()
    {
        var blockingBackend = new ControlledBlockingPlaybackBackend();
        using var keyState = new KeyStateManager(blockingBackend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 10);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;

        var timeline = new MusicTimeline("Terminating Track Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.5)); // pitch 60 -> 't'

        scheduler.SetTimeline(timeline);
        blockingBackend.SetBlockKey("t");
        scheduler.Play();

        await blockingBackend.WaitForBlockEnteredAsync();

        // While worker is blocked in KeyDown, start Stop in background
        var stopTask = Task.Run(() => scheduler.Stop());

        await Task.Delay(50);

        // HasActiveWorker MUST be true while terminating worker is still running!
        Assert.True(scheduler.HasActiveWorker, "HasActiveWorker must report true while terminating worker is alive");
        Assert.Equal(1, scheduler.LiveWorkerCount);
        Assert.False(stopTask.IsCompleted);

        // Release old worker
        blockingBackend.ReleaseBlock();
        await stopTask;

        Assert.False(scheduler.HasActiveWorker);
        Assert.Equal(0, scheduler.LiveWorkerCount);
        Assert.Equal(PlaybackState.Stopped, scheduler.State);
    }

    [Fact]
    public async Task Playback_DisposeTimeout_DoesNotDisposeWorkerSynchronizationPrimitives()
    {
        var blockingBackend = new ControlledBlockingPlaybackBackend();
        using var keyState = new KeyStateManager(blockingBackend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 10);

        // Configure short termination timeout (80ms) for fast unit test
        var scheduler = new PlaybackScheduler(engine, keyState, workerTerminationTimeout: TimeSpan.FromMilliseconds(80));
        scheduler.CountdownSeconds = 0;

        var timeline = new MusicTimeline("Dispose Timeout Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.5));

        scheduler.SetTimeline(timeline);
        blockingBackend.SetBlockKey("t");
        scheduler.Play();

        await blockingBackend.WaitForBlockEnteredAsync();

        // Invoking Dispose while worker is blocked must throw TimeoutException
        Assert.Throws<TimeoutException>(() => scheduler.Dispose());

        // Worker is still alive in blocked KeyDown: HasActiveWorker must be true and sync primitives intact
        Assert.True(scheduler.HasActiveWorker);
        Assert.Throws<ObjectDisposedException>(() => scheduler.Play()); // New playback blocked after shutdown

        // Unblock worker -> worker exits safely without ObjectDisposedException
        blockingBackend.ReleaseBlock();
        await Task.Delay(50);

        // Retry Dispose -> completes cleanly now that worker is terminated
        scheduler.Dispose();
        Assert.False(scheduler.HasActiveWorker);
    }

    [Fact]
    public async Task Playback_DisposeAsyncTimeout_DoesNotDestroyResourcesUsedByLiveWorker()
    {
        var blockingBackend = new ControlledBlockingPlaybackBackend();
        using var keyState = new KeyStateManager(blockingBackend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 10);

        var scheduler = new PlaybackScheduler(engine, keyState, workerTerminationTimeout: TimeSpan.FromMilliseconds(80));
        scheduler.CountdownSeconds = 0;

        var timeline = new MusicTimeline("DisposeAsync Timeout Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.5));

        scheduler.SetTimeline(timeline);
        blockingBackend.SetBlockKey("t");
        scheduler.Play();

        await blockingBackend.WaitForBlockEnteredAsync();

        await Assert.ThrowsAsync<TimeoutException>(async () => await scheduler.DisposeAsync());

        Assert.True(scheduler.HasActiveWorker);

        blockingBackend.ReleaseBlock();
        await Task.Delay(50);

        await scheduler.DisposeAsync();
        Assert.False(scheduler.HasActiveWorker);
    }

    [Fact]
    public async Task Playback_Replacement_DoesNotStartNewWorkerUntilOldWorkerExits()
    {
        var blockingBackend = new ControlledBlockingPlaybackBackend();
        using var keyState = new KeyStateManager(blockingBackend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 10);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;

        var timeline = new MusicTimeline("Blocking Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.5)); // Note 1: pitch 60 -> 't'
        timeline.AddNote(new NoteEvent(72, 1.0, 1.5)); // Note 2: pitch 72 -> 's'

        scheduler.SetTimeline(timeline);

        // Tell backend to block on 't'
        blockingBackend.SetBlockKey("t");
        scheduler.Play();

        // Wait until old worker hits block point in 't'
        await blockingBackend.WaitForBlockEnteredAsync();

        // While old worker is blocked, trigger Seek to 1.0s in background
        var seekTask = Task.Run(() => scheduler.Seek(1.0));

        // Verify: while old worker is blocked, seekTask has NOT completed and new note 's' has NOT been pressed
        await Task.Delay(50);
        Assert.False(seekTask.IsCompleted, "Seek must await old worker exit before completing");
        Assert.DoesNotContain(blockingBackend.Events, e => e.Key == "s");

        // Release old worker
        blockingBackend.ReleaseBlock();

        // Now seekTask completes and new worker starts
        await seekTask;

        for (int i = 0; i < 30; i++)
        {
            if (scheduler.State == PlaybackState.Completed) break;
            await Task.Delay(20);
        }

        Assert.Equal(PlaybackState.Completed, scheduler.State);
        Assert.Contains(blockingBackend.Events, e => e.Key == "s");
    }

    [Fact]
    public async Task Playback_Stop_ReturnsWithWorkerFullyTerminated()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 5);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;

        var timeline = new MusicTimeline("Stop Worker Test");
        for (int i = 0; i < 50; i++)
        {
            timeline.AddNote(new NoteEvent(60, i * 0.1, (i + 1) * 0.1));
        }

        scheduler.SetTimeline(timeline);
        scheduler.Play();

        await Task.Delay(50);
        scheduler.Stop();

        Assert.False(scheduler.HasActiveWorker, "Worker must be fully terminated before Stop returns");
        Assert.Equal(PlaybackState.Stopped, scheduler.State);
    }

    [Fact]
    public async Task Playback_Dispose_ReturnsWithWorkerFullyTerminated()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 5);
        var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;

        var timeline = new MusicTimeline("Dispose Worker Test");
        for (int i = 0; i < 50; i++)
        {
            timeline.AddNote(new NoteEvent(60, i * 0.1, (i + 1) * 0.1));
        }

        scheduler.SetTimeline(timeline);
        scheduler.Play();

        await Task.Delay(50);
        scheduler.Dispose();

        Assert.False(scheduler.HasActiveWorker, "Worker must be fully terminated before Dispose returns");
    }

    [Fact]
    public async Task Playback_Replacement_StaleGenerationCannotFireChordEnded()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 100);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;

        var timeline = new MusicTimeline("ChordEnded Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 1.0)); // Long note 1
        timeline.AddNote(new NoteEvent(72, 1.0, 1.5)); // Note 2

        scheduler.SetTimeline(timeline);

        int chordEndedCount = 0;
        scheduler.ChordEnded += (_, _) => chordEndedCount++;

        scheduler.Play();
        await Task.Delay(30);

        // While chord 1 is playing in gen 1, seek to 1.0s (gen 2)
        await scheduler.SeekAsync(1.0);

        for (int i = 0; i < 30; i++)
        {
            if (scheduler.State == PlaybackState.Completed) break;
            await Task.Delay(20);
        }

        Assert.Equal(PlaybackState.Completed, scheduler.State);
        // Only Note 2 from gen 2 should have fired ChordEnded! Gen 1's canceled chord must NOT fire ChordEnded
        Assert.Equal(1, chordEndedCount);
    }

    [Fact]
    public async Task Playback_Replacement_StaleGenerationCannotIncrementDiagnostics()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 100);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;

        var timeline = new MusicTimeline("Diagnostics Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 1.0)); // Gen 1 note

        scheduler.SetTimeline(timeline);
        scheduler.Play();
        await Task.Delay(30);

        // Replace with new timeline having 1 note
        var newTimeline = new MusicTimeline("New Gen");
        newTimeline.AddNote(new NoteEvent(72, 0.0, 0.05));
        await scheduler.SetTimelineAsync(newTimeline);

        scheduler.Play();

        for (int i = 0; i < 30; i++)
        {
            if (scheduler.State == PlaybackState.Completed) break;
            await Task.Delay(20);
        }

        Assert.Equal(PlaybackState.Completed, scheduler.State);
        // Played count must be exactly 1 (the new note), not 2
        Assert.Equal(1, scheduler.PlayedNoteCount);
    }

    [Fact]
    public async Task Playback_Seek_OldGenerationCannotPublishProgress()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 5);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;

        var timeline = new MusicTimeline("Progress Race");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.1));
        timeline.AddNote(new NoteEvent(72, 2.0, 2.1));

        scheduler.SetTimeline(timeline);

        var progressHistory = new List<double>();
        scheduler.ProgressChanged += (_, p) =>
        {
            lock (progressHistory)
            {
                progressHistory.Add(p.CurrentTime);
            }
        };

        scheduler.Play();
        await Task.Delay(100);

        // Seek backwards to 0.0s
        await scheduler.SeekAsync(0.0);

        lock (progressHistory)
        {
            progressHistory.Clear();
        }

        await Task.Delay(100);
        await scheduler.StopAsync();

        // After seek to 0.0s, no progress > 1.0s should have been published from old gen
        lock (progressHistory)
        {
            Assert.All(progressHistory, pos => Assert.True(pos < 0.8, $"Progress position was {pos:F3}s"));
        }
    }

    [Fact]
    public async Task Playback_RapidSeek_OnlyLastGenerationRemainsActive()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 5);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;

        var timeline = new MusicTimeline("Rapid Seek");
        for (int i = 0; i < 50; i++)
        {
            timeline.AddNote(new NoteEvent(60 + (i % 12), i * 0.1, (i + 1) * 0.1));
        }

        scheduler.SetTimeline(timeline);
        scheduler.Play();

        for (int i = 0; i < 15; i++)
        {
            double pos = (i % 5) * 0.5;
            await scheduler.SeekAsync(pos);
            await Task.Delay(5);
        }

        Assert.True(scheduler.HasActiveWorker || scheduler.State == PlaybackState.Playing);
        await scheduler.StopAsync();
        Assert.False(scheduler.HasActiveWorker);
        Assert.Empty(keyState.ActiveKeys);
    }

    [Fact]
    public async Task LoadNewScoreDuringActiveChord_OldGenerationFullyStops()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 200);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;

        var oldTimeline = new MusicTimeline("Old Song");
        oldTimeline.AddNote(new NoteEvent(60, 0.0, 1.0)); // pitch 60 ('t')

        scheduler.SetTimeline(oldTimeline);
        scheduler.Play();
        await Task.Delay(30);

        var newTimeline = new MusicTimeline("New Song");
        newTimeline.AddNote(new NoteEvent(72, 0.0, 0.1)); // pitch 72 ('s')

        await scheduler.SetTimelineAsync(newTimeline);

        Assert.False(scheduler.HasActiveWorker);
        Assert.Equal(PlaybackState.Idle, scheduler.State);

        int countAfterSet = backend.Events.Count;
        await Task.Delay(150);

        Assert.Equal(countAfterSet, backend.Events.Count);
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

        // Timeline: 0.0 -> pitch 53 ('q'), 0.2 -> pitch 55 ('w'), 1.0 -> pitch 57 ('e'), 1.2 -> pitch 59 ('r')
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

        var timeline = new MusicTimeline("Speed Gap Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.1)); // 't'
        timeline.AddNote(new NoteEvent(72, 2.0, 2.1)); // 's'

        scheduler.SetTimeline(timeline);

        long start = Stopwatch.GetTimestamp();
        scheduler.Play();

        await Task.Delay(200);
        scheduler.Speed = 4.0;

        for (int i = 0; i < 40; i++)
        {
            if (scheduler.State == PlaybackState.Completed) break;
            await Task.Delay(30);
        }

        double elapsedSeconds = (double)(Stopwatch.GetTimestamp() - start) / Stopwatch.Frequency;

        Assert.Equal(PlaybackState.Completed, scheduler.State);
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

        var timeline = new MusicTimeline("Progress Gap Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.1));
        timeline.AddNote(new NoteEvent(72, 3.0, 3.1));

        scheduler.SetTimeline(timeline);
        scheduler.Play();

        await Task.Delay(500);

        double progressInGap = scheduler.CurrentTime;
        scheduler.Stop();

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
        Assert.True(elapsedSeconds >= 0.25 && elapsedSeconds <= 1.20, $"100 events measured duration was {elapsedSeconds:F3}s");
    }

    private class ControlledBlockingPlaybackBackend : IPlaybackBackend
    {
        private string? _blockKey;
        private readonly TaskCompletionSource _blockEnteredTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _releaseEvent = new(false);

        public List<PlaybackBackendEvent> Events { get; } = new();

        public void SetBlockKey(string key) => _blockKey = key;
        public Task WaitForBlockEnteredAsync() => _blockEnteredTcs.Task;
        public void ReleaseBlock() => _releaseEvent.Set();

        public void KeyDown(string key)
        {
            Events.Add(new PlaybackBackendEvent(Stopwatch.GetTimestamp(), BackendAction.KeyDown, key));
            if (string.Equals(key, _blockKey, StringComparison.OrdinalIgnoreCase))
            {
                _blockEnteredTcs.TrySetResult();
                _releaseEvent.Wait(TimeSpan.FromSeconds(5));
            }
        }

        public void KeyUp(string key)
        {
            Events.Add(new PlaybackBackendEvent(Stopwatch.GetTimestamp(), BackendAction.KeyUp, key));
        }

        public void ReleaseAll() { }

        public void Dispose()
        {
            _releaseEvent.Dispose();
        }
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
