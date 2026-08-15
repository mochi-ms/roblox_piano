using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using RobloxPiano.Core.Importing;
using RobloxPiano.Core.Music;
using Xunit;

namespace RobloxPiano.Core.Tests;

public class ImportPipelineTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ImportPipeline _pipeline;

    public ImportPipelineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "rp_pipeline_tests_" + Guid.NewGuid().ToString("N"));
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

    private string CreateValidMidiFile(string filename = "valid.mid", int pitch = 60, int durationTicks = 480)
    {
        string path = Path.Combine(_tempDir, filename);
        var midiFile = new MidiFile(new TrackChunk(
            new NoteOnEvent((SevenBitNumber)pitch, (SevenBitNumber)64) { DeltaTime = 0 },
            new NoteOffEvent((SevenBitNumber)pitch, (SevenBitNumber)0) { DeltaTime = durationTicks }
        ))
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480)
        };
        midiFile.Write(path, true);
        return path;
    }

    [Fact]
    public async Task ImportPipeline_ValidMidi_ReturnsTimeline()
    {
        string path = CreateValidMidiFile("piano_solo.mid", pitch: 60, durationTicks: 480);
        var req = new ImportRequest(path, addToLibrary: false);

        var result = await _pipeline.ImportFileAsync(req);

        Assert.True(result.Success);
        Assert.Equal(ImportSourceType.Midi, result.SourceType);
        Assert.NotNull(result.Timeline);
        Assert.Single(result.Timeline.Notes);
        Assert.Equal(60, result.Timeline.Notes[0].Pitch);
        Assert.Equal(0.5, result.Duration, precision: 3);
        Assert.Equal(1, result.PlayableNoteCount);
        Assert.Equal(0, result.OutOfRangeNoteCount);
        Assert.Equal(60, result.MinPitch);
        Assert.Equal(60, result.MaxPitch);
    }

    [Fact]
    public async Task ImportPipeline_ValidMml_ReturnsTimeline()
    {
        string mmlPath = Path.Combine(_tempDir, "song.mml");
        await File.WriteAllTextAsync(mmlPath, "MML@T120L4CDEF;");
        var req = new ImportRequest(mmlPath, addToLibrary: false);

        var result = await _pipeline.ImportFileAsync(req);

        Assert.True(result.Success);
        Assert.Equal(ImportSourceType.Mml, result.SourceType);
        Assert.NotNull(result.Timeline);
        Assert.Equal(4, result.Timeline.Notes.Count);
        Assert.Equal(4, result.PlayableNoteCount);
        Assert.Equal(0, result.OutOfRangeNoteCount);
    }

    [Fact]
    public async Task ImportPipeline_MissingFile_ReturnsFailure()
    {
        var req = new ImportRequest(Path.Combine(_tempDir, "non_existent.mid"), addToLibrary: false);

        var result = await _pipeline.ImportFileAsync(req);

        Assert.False(result.Success);
        Assert.Equal(ImportError.FileNotFound, result.ErrorMessage);
    }

    [Fact]
    public async Task ImportPipeline_CorruptMidi_ReturnsFailureWithoutThrowing()
    {
        string corruptPath = Path.Combine(_tempDir, "corrupted.mid");
        await File.WriteAllBytesAsync(corruptPath, new byte[] { 0x4D, 0x54, 0x68, 0x64, 0x00, 0x00, 0xFF }); // Corrupt header chunk
        var req = new ImportRequest(corruptPath, addToLibrary: false);

        var result = await _pipeline.ImportFileAsync(req);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task ImportPipeline_InvalidMml_ReturnsFailureWithoutThrowing()
    {
        string invalidMmlPath = Path.Combine(_tempDir, "broken.mml");
        await File.WriteAllTextAsync(invalidMmlPath, "MML@T9999999999999999999999999999999999999;");
        var req = new ImportRequest(invalidMmlPath, addToLibrary: false);

        var result = await _pipeline.ImportFileAsync(req);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task ImportPipeline_ZeroNoteScore_ReturnsExpectedPolicy()
    {
        string emptyMidiPath = Path.Combine(_tempDir, "empty_tracks.mid");
        var emptyMidi = new MidiFile(new TrackChunk(
            new TextEvent("Track with no notes") { DeltaTime = 0 }
        ));
        emptyMidi.Write(emptyMidiPath, true);

        var req = new ImportRequest(emptyMidiPath, addToLibrary: false);
        var result = await _pipeline.ImportFileAsync(req);

        Assert.False(result.Success);
        Assert.Equal(ImportError.NoPlayableNotes, result.ErrorMessage);
    }

    [Fact]
    public async Task ImportPipeline_OutOf61KeyRange_RemainsSuccessfulWithDiagnostics()
    {
        string wideRangeMidi = Path.Combine(_tempDir, "wide_range.mid");
        var midiFile = new MidiFile(new TrackChunk(
            new NoteOnEvent((SevenBitNumber)21, (SevenBitNumber)64) { DeltaTime = 0 },
            new NoteOffEvent((SevenBitNumber)21, (SevenBitNumber)0) { DeltaTime = 480 },
            new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)64) { DeltaTime = 0 },
            new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 480 },
            new NoteOnEvent((SevenBitNumber)108, (SevenBitNumber)64) { DeltaTime = 0 },
            new NoteOffEvent((SevenBitNumber)108, (SevenBitNumber)0) { DeltaTime = 480 }
        ))
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480)
        };
        midiFile.Write(wideRangeMidi, true);

        var req = new ImportRequest(wideRangeMidi, addToLibrary: false);
        var result = await _pipeline.ImportFileAsync(req);

        Assert.True(result.Success);
        Assert.Equal(3, result.NoteCount);
        Assert.Equal(1, result.PlayableNoteCount);
        Assert.Equal(2, result.OutOfRangeNoteCount);
        Assert.Equal(21, result.MinPitch);
        Assert.Equal(108, result.MaxPitch);
    }

    [Fact]
    public async Task ImportPipeline_UnicodeFilename_Works()
    {
        string filename = "피아노_연주_곡_테스트.mid";
        string unicodePath = CreateValidMidiFile(filename, pitch: 64, durationTicks: 480);

        var req = new ImportRequest(unicodePath, addToLibrary: false);
        var result = await _pipeline.ImportFileAsync(req);

        Assert.True(result.Success);
        Assert.Equal("피아노_연주_곡_테스트", result.Title);
        Assert.Single(result.Timeline!.Notes);
    }

    [Fact]
    public async Task ImportPipeline_MmlCanonicalRegression_T150L16N58L8GG()
    {
        string mmlFile = Path.Combine(_tempDir, "canonical.mml");
        await File.WriteAllTextAsync(mmlFile, "MML@T150L16N58L8GG;");

        var req = new ImportRequest(mmlFile, addToLibrary: false);
        var result = await _pipeline.ImportFileAsync(req);

        Assert.True(result.Success);
        Assert.NotNull(result.Timeline);
        Assert.Equal(3, result.Timeline.Notes.Count);

        var note0 = result.Timeline.Notes[0];
        var note1 = result.Timeline.Notes[1];
        var note2 = result.Timeline.Notes[2];

        Assert.Equal(58, note0.Pitch);
        Assert.Equal(0.0, note0.StartTime, precision: 3);
        Assert.Equal(0.10, note0.Duration, precision: 3);

        Assert.Equal(0.10, note1.StartTime, precision: 3);
        Assert.Equal(0.20, note1.Duration, precision: 3);

        Assert.Equal(0.30, note2.StartTime, precision: 3);
        Assert.Equal(0.20, note2.Duration, precision: 3);

        Assert.Equal(0.50, result.Timeline.Duration, precision: 3);
    }

    [Fact]
    public void ImportValidation_NaNBpm_IsRejected()
    {
        var timeline = new MusicTimeline("Corrupted BPM");
        timeline.AddNote(new RobloxPiano.Core.Music.NoteEvent(60, 0.0, 0.5));
        timeline.InitialBpm = double.NaN;

        var validation = ImportTimelineValidator.Validate(timeline);

        Assert.False(validation.IsValid);
        Assert.Equal(ImportError.CorruptTiming, validation.ErrorMessage);
    }

    [Fact]
    public void ImportValidation_InfiniteBpm_IsRejected()
    {
        var timeline = new MusicTimeline("Infinite BPM");
        timeline.AddNote(new RobloxPiano.Core.Music.NoteEvent(60, 0.0, 0.5));
        timeline.InitialBpm = double.PositiveInfinity;

        var validation = ImportTimelineValidator.Validate(timeline);

        Assert.False(validation.IsValid);
        Assert.Equal(ImportError.CorruptTiming, validation.ErrorMessage);
    }

    [Fact]
    public void ImportValidation_ZeroOrNegativeBpm_IsRejected()
    {
        var timelineZero = new MusicTimeline("Zero BPM");
        timelineZero.AddNote(new RobloxPiano.Core.Music.NoteEvent(60, 0.0, 0.5));
        timelineZero.InitialBpm = 0.0;

        var valZero = ImportTimelineValidator.Validate(timelineZero);
        Assert.False(valZero.IsValid);
        Assert.Equal(ImportError.CorruptTiming, valZero.ErrorMessage);

        var timelineNeg = new MusicTimeline("Neg BPM");
        timelineNeg.AddNote(new RobloxPiano.Core.Music.NoteEvent(60, 0.0, 0.5));
        timelineNeg.InitialBpm = -120.0;

        var valNeg = ImportTimelineValidator.Validate(timelineNeg);
        Assert.False(valNeg.IsValid);
        Assert.Equal(ImportError.CorruptTiming, valNeg.ErrorMessage);
    }

    [Fact]
    public void ImportValidation_NaNNoteTiming_IsRejected()
    {
        var timeline = new MusicTimeline("NaN Note Timing");
        timeline.AddNote(new RobloxPiano.Core.Music.NoteEvent(60, double.NaN, 0.5));

        var validation = ImportTimelineValidator.Validate(timeline);

        Assert.False(validation.IsValid);
        Assert.Equal(ImportError.CorruptTiming, validation.ErrorMessage);
    }

    [Fact]
    public void ImportValidation_InfiniteNoteTiming_IsRejected()
    {
        var timeline = new MusicTimeline("Infinite Note Timing");
        timeline.AddNote(new RobloxPiano.Core.Music.NoteEvent(60, 0.0, double.PositiveInfinity));

        var validation = ImportTimelineValidator.Validate(timeline);

        Assert.False(validation.IsValid);
        Assert.Equal(ImportError.CorruptTiming, validation.ErrorMessage);
    }
}
