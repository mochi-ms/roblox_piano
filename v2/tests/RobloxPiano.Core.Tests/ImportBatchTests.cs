using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using RobloxPiano.Core.Importing;
using Xunit;

namespace RobloxPiano.Core.Tests;

public class ImportBatchTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ImportPipeline _pipeline;

    public ImportBatchTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "rp_batch_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _pipeline = new ImportPipeline();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    private string CreateMidi(string name, int pitch = 60)
    {
        string p = Path.Combine(_tempDir, name);
        var midiFile = new MidiFile(new TrackChunk(
            new NoteOnEvent((SevenBitNumber)pitch, (SevenBitNumber)64) { DeltaTime = 0 },
            new NoteOffEvent((SevenBitNumber)pitch, (SevenBitNumber)0) { DeltaTime = 480 }
        ))
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480)
        };
        midiFile.Write(p, true);
        return p;
    }

    private string CreateMml(string name, string mml)
    {
        string p = Path.Combine(_tempDir, name);
        File.WriteAllText(p, mml);
        return p;
    }

    [Fact]
    public async Task ImportBatch_AllValid_AllSucceed()
    {
        var f1 = CreateMidi("song1.mid", 60);
        var f2 = CreateMidi("song2.mid", 64);
        var f3 = CreateMml("song3.mml", "MML@T120L4C;");

        var requests = new[]
        {
            new ImportRequest(f1, addToLibrary: false),
            new ImportRequest(f2, addToLibrary: false),
            new ImportRequest(f3, addToLibrary: false)
        };

        var batchResult = await _pipeline.ImportBatchAsync(requests);

        Assert.Equal(3, batchResult.TotalCount);
        Assert.Equal(3, batchResult.SuccessCount);
        Assert.Equal(0, batchResult.FailureCount);
        Assert.True(batchResult.IsAllSuccessful);
        Assert.False(batchResult.IsCancelled);
    }

    [Fact]
    public async Task ImportBatch_MixedMidiMml_Succeeds()
    {
        var f1 = CreateMidi("piano.mid", 60);
        var f2 = CreateMml("sheet.txt", "MML@T120L4E;");

        var requests = new[]
        {
            new ImportRequest(f1, addToLibrary: false),
            new ImportRequest(f2, addToLibrary: false)
        };

        var batchResult = await _pipeline.ImportBatchAsync(requests);

        Assert.Equal(2, batchResult.TotalCount);
        Assert.Equal(2, batchResult.SuccessCount);
        Assert.Equal(ImportSourceType.Midi, batchResult.Results[0].SourceType);
        Assert.Equal(ImportSourceType.Mml, batchResult.Results[1].SourceType);
    }

    [Fact]
    public async Task ImportBatch_OneBadFile_OthersStillSucceed()
    {
        var f1 = CreateMidi("good1.mid", 60);
        var badFile = Path.Combine(_tempDir, "corrupt.mid");
        File.WriteAllText(badFile, "Corrupt not midi");
        var f3 = CreateMidi("good2.mid", 67);

        var requests = new[]
        {
            new ImportRequest(f1, addToLibrary: false),
            new ImportRequest(badFile, addToLibrary: false),
            new ImportRequest(f3, addToLibrary: false)
        };

        var batchResult = await _pipeline.ImportBatchAsync(requests);

        Assert.Equal(3, batchResult.TotalCount);
        Assert.Equal(2, batchResult.SuccessCount);
        Assert.Equal(1, batchResult.FailureCount);
        Assert.False(batchResult.IsAllSuccessful);

        Assert.True(batchResult.Results[0].Success);
        Assert.False(batchResult.Results[1].Success);
        Assert.True(batchResult.Results[2].Success);
    }

    [Fact]
    public async Task ImportBatch_UnsupportedFile_ReportsPerFileFailure()
    {
        var f1 = CreateMidi("valid.mid", 60);
        var f2 = Path.Combine(_tempDir, "track.mp3");
        File.WriteAllText(f2, "Fake audio");

        var requests = new[]
        {
            new ImportRequest(f1, addToLibrary: false),
            new ImportRequest(f2, addToLibrary: false)
        };

        var batchResult = await _pipeline.ImportBatchAsync(requests);

        Assert.Equal(2, batchResult.TotalCount);
        Assert.Equal(1, batchResult.SuccessCount);
        Assert.Equal(1, batchResult.FailureCount);
        Assert.Equal(ImportError.UnsupportedFormat, batchResult.Results[1].ErrorMessage);
    }

    [Fact]
    public async Task ImportBatch_Cancellation_StopsCleanly()
    {
        var f1 = CreateMidi("file1.mid", 60);
        var f2 = CreateMidi("file2.mid", 62);
        var f3 = CreateMidi("file3.mid", 64);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancel

        var requests = new[]
        {
            new ImportRequest(f1, addToLibrary: false),
            new ImportRequest(f2, addToLibrary: false),
            new ImportRequest(f3, addToLibrary: false)
        };

        var batchResult = await _pipeline.ImportBatchAsync(requests, ct: cts.Token);

        Assert.Equal(3, batchResult.TotalCount);
        Assert.True(batchResult.IsCancelled);
        Assert.All(batchResult.Results, r => Assert.Equal(ImportError.Cancelled, r.ErrorMessage));
    }

    [Fact]
    public async Task ImportBatch_CancellationDuringBatch_StopsRemainingFilesCleanly()
    {
        var f1 = CreateMidi("file1.mid", 60);
        var f2 = CreateMidi("file2.mid", 62);
        var f3 = CreateMidi("file3.mid", 64);

        using var cts = new CancellationTokenSource();

        var requests = new[]
        {
            new ImportRequest(f1, addToLibrary: false),
            new ImportRequest(f2, addToLibrary: false),
            new ImportRequest(f3, addToLibrary: false)
        };

        // Custom synchronizing progress that triggers cancellation when file 2 starts
        var syncProgress = new SynchronousProgress<(int Current, int Total, string FileName)>(report =>
        {
            if (report.Current >= 2)
            {
                cts.Cancel();
            }
        });

        var batchResult = await _pipeline.ImportBatchAsync(requests, progress: syncProgress, ct: cts.Token);

        Assert.Equal(3, batchResult.TotalCount);
        Assert.True(batchResult.IsCancelled);
        Assert.True(batchResult.Results[0].Success);
        Assert.False(batchResult.Results[1].Success);
        Assert.Equal(ImportError.Cancelled, batchResult.Results[1].ErrorMessage);
        Assert.False(batchResult.Results[2].Success);
        Assert.Equal(ImportError.Cancelled, batchResult.Results[2].ErrorMessage);
    }

    private class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;
        public SynchronousProgress(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }
}
