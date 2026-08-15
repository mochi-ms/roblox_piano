using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using RobloxPiano.Core.Importing;
using RobloxPiano.Core.Library;
using RobloxPiano.Desktop.ViewModels;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class ImportViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ImportPipeline _pipeline;

    public ImportViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"rp_import_vm_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _pipeline = new ImportPipeline();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    private string CreateMidi(string filename, int pitch = 60)
    {
        string path = Path.Combine(_tempDir, filename);
        var midiFile = new MidiFile(new TrackChunk(
            new NoteOnEvent((SevenBitNumber)pitch, (SevenBitNumber)64) { DeltaTime = 0 },
            new NoteOffEvent((SevenBitNumber)pitch, (SevenBitNumber)0) { DeltaTime = 480 }
        ))
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480)
        };
        midiFile.Write(path, true);
        return path;
    }

    [Fact]
    public void ImportViewModel_AddFiles_ShowsQueue()
    {
        using var vm = new ImportViewModel(_pipeline);
        var f1 = CreateMidi("song1.mid");
        var f2 = CreateMidi("song2.mid");

        vm.AddFiles(new[] { f1, f2 });

        Assert.True(vm.HasItems);
        Assert.Equal(2, vm.QueueItems.Count);
        Assert.Equal("song1.mid", vm.QueueItems[0].FileName);
        Assert.Equal("song2.mid", vm.QueueItems[1].FileName);
        Assert.Equal(ImportItemStatus.Pending, vm.QueueItems[0].Status);
    }

    [Fact]
    public async Task ImportViewModel_Import_UpdatesStatuses()
    {
        using var vm = new ImportViewModel(_pipeline);
        var f1 = CreateMidi("song.mid");

        vm.AddFiles(new[] { f1 });
        await vm.StartImportAsync();

        Assert.Single(vm.QueueItems);
        var item = vm.QueueItems[0];
        Assert.Equal(ImportItemStatus.Completed, item.Status);
        Assert.Equal("완료", item.StatusText);
        Assert.True(item.IsCompleted);
        Assert.False(item.IsFailed);
        Assert.Equal("1개 가져오기 완료 · 0개 실패", vm.SummaryText);
    }

    [Fact]
    public async Task ImportViewModel_MixedSuccessFailure_ReportsCounts()
    {
        using var vm = new ImportViewModel(_pipeline);
        var f1 = CreateMidi("valid.mid");
        var f2 = Path.Combine(_tempDir, "bad.mid");
        File.WriteAllText(f2, "Not midi");

        vm.AddFiles(new[] { f1, f2 });
        await vm.StartImportAsync();

        Assert.Equal(2, vm.QueueItems.Count);
        Assert.Equal(ImportItemStatus.Completed, vm.QueueItems[0].Status);
        Assert.Equal(ImportItemStatus.Failed, vm.QueueItems[1].Status);
        Assert.Equal("1개 가져오기 완료 · 1개 실패", vm.SummaryText);
    }

    [Fact]
    public void ImportViewModel_Cancel_UpdatesState()
    {
        using var vm = new ImportViewModel(_pipeline);
        var f1 = CreateMidi("song1.mid");
        vm.AddFiles(new[] { f1 });

        // Cancel when idle does not crash
        vm.CancelImport();
        Assert.False(vm.IsImporting);
    }

    [Fact]
    public async Task ImportViewModel_OpenImportedScore_RaisesNavigationIntent()
    {
        using var vm = new ImportViewModel(_pipeline);
        var f1 = CreateMidi("play_me.mid");
        vm.AddFiles(new[] { f1 });

        await vm.StartImportAsync();

        ScoreItem? openedScore = null;
        vm.OpenScoreRequested += (_, score) =>
        {
            openedScore = score;
        };

        var completedItem = vm.QueueItems[0];
        vm.OpenInPlayer(completedItem);

        Assert.NotNull(openedScore);
        Assert.Equal("play_me", openedScore.Title);
    }
}
