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
}
