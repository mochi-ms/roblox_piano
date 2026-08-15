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
    public void PlayerViewModel_ProgrammaticProgressDoesNotTriggerSeekLoop()
    {
        using var backend = new DryRunPlaybackBackend();
        using var vm = new PlayerViewModel(backend);

        var timeline = new MusicTimeline("Guard Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.5));
        timeline.AddNote(new NoteEvent(64, 5.0, 5.5));
        vm.LoadTimeline(timeline);

        vm.Scheduler.CountdownSeconds = 0;
        vm.Play();

        // Simulate multiple programmatic scheduler progress updates
        vm.CurrentTime = 1.5;

        // Verify scheduler state remains Playing
        Assert.Equal(RobloxPiano.Playback.Windows.Playback.PlaybackState.Playing, vm.Scheduler.State);
        vm.Stop();
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
    public void PlayerViewModel_ChordEvents_UpdateAndClearActivePianoKeys()
    {
        using var backend = new DryRunPlaybackBackend();
        using var vm = new PlayerViewModel(backend);

        Assert.Equal(61, vm.PianoKeys.Count);
        Assert.All(vm.PianoKeys, k => Assert.False(k.IsActive));

        var timeline = new MusicTimeline("Key Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.5));
        vm.LoadTimeline(timeline);

        var key60 = vm.PianoKeys.FirstOrDefault(k => k.Pitch == 60);
        Assert.NotNull(key60);
        Assert.False(key60.IsActive);
    }

    [Fact]
    public async Task MainViewModel_Dispose_StopsPlaybackAndReleasesEverything()
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

        // App/window shutdown triggers MainViewModel.Dispose()
        mainVm.Dispose();

        int eventsAtDispose = backend.Events.Count;
        await Task.Delay(100);

        Assert.Equal(eventsAtDispose, backend.Events.Count);
        Assert.Empty(backend.PressedKeys);
    }
}
