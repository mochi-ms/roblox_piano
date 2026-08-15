using RobloxPiano.Core.Library;
using RobloxPiano.Core.Music;
using RobloxPiano.Desktop.ViewModels;
using RobloxPiano.Playback.Windows.Input;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class PlayerViewModelPlaybackTests : IDisposable
{
    private readonly string _tempDir;

    public PlayerViewModelPlaybackTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"player_vm_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task LoadScoreAsync_MmlScore_PopulatesMetadataAndTimeline()
    {
        string mmlFile = Path.Combine(_tempDir, "sample.mml");
        await File.WriteAllTextAsync(mmlFile, "MML@t120l4cdefgab>c;");

        var score = new ScoreItem("s-test", "Sample MML Song", "MML", "", mmlFile);

        using var backend = new DryRunPlaybackBackend();
        using var vm = new PlayerViewModel(backend);

        await vm.LoadScoreAsync(score);

        Assert.True(vm.HasScore);
        Assert.Equal("Sample MML Song", vm.Title);
        Assert.Equal("MML", vm.SourceType);
        Assert.Equal("120", vm.FormattedBpm);
        Assert.Equal("8", vm.FormattedTotalNotes);
        Assert.NotNull(vm.CurrentTimeline);
    }

    [Fact]
    public async Task LoadScoreAsync_NonExistentFile_SetsErrorStatus()
    {
        var score = new ScoreItem("s-missing", "Missing Song", "MIDI", "", Path.Combine(_tempDir, "non_existent.mid"));

        using var backend = new DryRunPlaybackBackend();
        using var vm = new PlayerViewModel(backend);

        await vm.LoadScoreAsync(score);

        Assert.False(vm.HasScore);
        Assert.Equal("악보 파일을 찾을 수 없습니다.", vm.StatusText);
    }

    [Fact]
    public void TransportCommands_PlayPauseStop_UpdatesStateAndStatusText()
    {
        using var backend = new DryRunPlaybackBackend();
        using var vm = new PlayerViewModel(backend);

        var timeline = new MusicTimeline("Transport Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 1.0));
        vm.LoadTimeline(timeline, "Transport Test", "MIDI");

        Assert.True(vm.HasScore);
        Assert.Equal("준비됨", vm.StatusText);

        vm.Scheduler.CountdownSeconds = 0;
        vm.Play();
        Assert.Equal(RobloxPiano.Playback.Windows.Playback.PlaybackState.Playing, vm.Scheduler.State);

        vm.Pause();
        Assert.Equal(RobloxPiano.Playback.Windows.Playback.PlaybackState.Paused, vm.Scheduler.State);

        vm.Stop();
        Assert.Equal(RobloxPiano.Playback.Windows.Playback.PlaybackState.Stopped, vm.Scheduler.State);
    }

    [Fact]
    public void SpeedAndTranspose_UpdatesSchedulerOptions()
    {
        using var backend = new DryRunPlaybackBackend();
        using var vm = new PlayerViewModel(backend);

        vm.SelectedSpeed = 1.5;
        Assert.Equal(1.5, vm.Scheduler.Speed);

        vm.SelectedTranspose = 2;
        Assert.Equal(2, vm.Scheduler.Transpose);
    }

    [Fact]
    public void PlayerViewModel_LoadTimeline_BuildsRealPianoRollNotes()
    {
        using var backend = new DryRunPlaybackBackend();
        using var vm = new PlayerViewModel(backend);

        var timeline = new MusicTimeline("Piano Roll Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.5, hand: HandType.Right));
        timeline.AddNote(new NoteEvent(64, 0.5, 1.0, hand: HandType.Right));
        timeline.AddNote(new NoteEvent(48, 0.0, 1.0, hand: HandType.Left));

        vm.LoadTimeline(timeline, "Piano Roll Test", "MIDI");

        Assert.Equal(3, vm.PianoRollNotes.Count);

        var n1 = vm.PianoRollNotes[0];
        Assert.Equal(60, n1.Pitch);
        Assert.Equal(0.0, n1.CanvasLeft);
        Assert.True(n1.Width > 0);
        Assert.Equal("#5B8DEF", n1.ColorBrushKey); // RH Accent

        var n3 = vm.PianoRollNotes[2];
        Assert.Equal(48, n3.Pitch);
        Assert.Equal("#34D399", n3.ColorBrushKey); // LH Success
    }

    [Fact]
    public async Task PlayerViewModel_RealChordLifecycle_ActivatesAndClearsPianoKey()
    {
        using var backend = new DryRunPlaybackBackend();
        using var vm = new PlayerViewModel(backend);

        var timeline = new MusicTimeline("Key Activation Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.15));

        vm.LoadTimeline(timeline);
        vm.Scheduler.CountdownSeconds = 0;

        var key60 = vm.PianoKeys.First(k => k.Pitch == 60);
        Assert.False(key60.IsActive);

        bool wasActiveDuringChord = false;
        vm.Scheduler.ChordStarted += (_, notes) =>
        {
            if (notes.Any(n => n.Pitch == 60))
            {
                wasActiveDuringChord = key60.IsActive;
            }
        };

        vm.Play();

        for (int i = 0; i < 30; i++)
        {
            if (vm.Scheduler.State == RobloxPiano.Playback.Windows.Playback.PlaybackState.Completed) break;
            if (key60.IsActive) wasActiveDuringChord = true;
            await Task.Delay(15);
        }

        Assert.True(wasActiveDuringChord, "Key 60 should have been active during chord execution");
        Assert.False(key60.IsActive, "Key 60 must be cleared after chord completion");
    }

    [Fact]
    public async Task PlayerViewModel_TransposedChordLifecycle_ActivatesTransposedPianoKey()
    {
        using var backend = new DryRunPlaybackBackend();
        using var vm = new PlayerViewModel(backend);

        var timeline = new MusicTimeline("Transpose Key Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.15)); // Pitch 60 + transpose 12 -> Pitch 72

        vm.LoadTimeline(timeline);
        vm.SelectedTranspose = 12;
        vm.Scheduler.CountdownSeconds = 0;

        var key60 = vm.PianoKeys.First(k => k.Pitch == 60);
        var key72 = vm.PianoKeys.First(k => k.Pitch == 72);

        bool was72Active = false;
        bool was60Active = false;

        vm.Play();

        for (int i = 0; i < 30; i++)
        {
            if (vm.Scheduler.State == RobloxPiano.Playback.Windows.Playback.PlaybackState.Completed) break;
            if (key72.IsActive) was72Active = true;
            if (key60.IsActive) was60Active = true;
            await Task.Delay(15);
        }

        Assert.True(was72Active, "Transposed key 72 should have been active");
        Assert.False(was60Active, "Untransposed key 60 must not be active");
    }

    [Fact]
    public async Task PlayerViewModel_ActualSchedulerProgressDoesNotCauseSeekFeedback()
    {
        using var backend = new DryRunPlaybackBackend();
        using var vm = new PlayerViewModel(backend);

        var timeline = new MusicTimeline("Progress Feedback Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.2));
        timeline.AddNote(new NoteEvent(64, 0.4, 0.6));

        vm.LoadTimeline(timeline);
        vm.Scheduler.CountdownSeconds = 0;

        int initialSeekCount = vm.Scheduler.SeekInvocationCount;
        vm.Play();

        // Allow real progress events to fire
        await Task.Delay(150);

        // Scheduler progress events must NOT have invoked Seek on scheduler
        Assert.Equal(initialSeekCount, vm.Scheduler.SeekInvocationCount);

        // Manually invoke user seek
        vm.Seek(0.1);
        Assert.Equal(initialSeekCount + 1, vm.Scheduler.SeekInvocationCount);

        vm.Stop();
    }

    [Fact]
    public async Task MainViewModel_Shutdown_FullyTerminatesPlaybackWorker()
    {
        using var backend = new DryRunPlaybackBackend();
        var playerVm = new PlayerViewModel(backend);
        var mainVm = new MainViewModel(playerVm);

        var timeline = new MusicTimeline("Shutdown Test");
        for (int i = 0; i < 30; i++)
        {
            timeline.AddNote(new NoteEvent(60, i * 0.1, (i + 1) * 0.1));
        }
        playerVm.LoadTimeline(timeline);
        playerVm.Scheduler.CountdownSeconds = 0;
        playerVm.Play();

        await Task.Delay(50);

        // Window / App shutdown triggers MainViewModel.Dispose()
        mainVm.Dispose();

        Assert.False(playerVm.Scheduler.HasActiveWorker);
        int eventsAtDispose = backend.Events.Count;
        await Task.Delay(100);

        Assert.Equal(eventsAtDispose, backend.Events.Count);
        Assert.Empty(backend.PressedKeys);
    }
}
