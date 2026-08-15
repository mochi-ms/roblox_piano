using Melanchall.DryWetMidi.Core;
using RobloxPiano.Core.Importers;
using Xunit;

namespace RobloxPiano.Core.Tests;

public class MmlImporterTests
{
    private readonly MmlImporter _importer = new();

    [Fact]
    public void Mml_Metadata_ExtractsTracksAndTempo()
    {
        string mmlCode = "MML@T131V15L16>A>C+A2A8.G+8F+8,L8C-A-B-C;";
        var meta = _importer.ExtractMetadata(mmlCode);

        Assert.Equal(2, Convert.ToInt32(meta["tracks"]));
        Assert.Equal(131, Convert.ToInt32(meta["tempo"]));
    }

    [Fact]
    public void Mml_Conversion_WritesValidMidiFile()
    {
        string mmlCode = "MML@T131V15L16O4CDEFGAB>C<C,R4E4G4;";
        string tempMid = Path.Combine(Path.GetTempPath(), $"test_mml_conv_{Guid.NewGuid():N}.mid");
        try
        {
            _importer.ConvertToMidi(mmlCode, tempMid);
            Assert.True(File.Exists(tempMid));

            var mid = MidiFile.Read(tempMid);
            Assert.Equal(2, mid.GetTrackChunks().Count());
        }
        finally
        {
            if (File.Exists(tempMid)) File.Delete(tempMid);
        }
    }

    [Fact]
    public void Mml_Tie_CreatesSingleLogicalNoteOn()
    {
        string mmlCode = "MML@A2&A4;";
        var (mid, _) = _importer.ParseToMidi(mmlCode);
        var track = (TrackChunk)mid.Chunks[0];
        var noteOns = track.Events.OfType<NoteOnEvent>().Where(n => n.Velocity > 0).ToList();

        Assert.Single(noteOns);
    }

    [Fact]
    public void Mml_InvalidTokens_ThrowsMmlParseExceptionWithContext()
    {
        string mmlCode = "MML@ T120 O4 C X Y Z4 ;";
        var ex = Assert.Throws<MmlParseException>(() => _importer.ParseToMidi(mmlCode));

        Assert.Equal("X", ex.Token);
        Assert.Contains("Unexpected token 'X'", ex.Message);
    }

    [Fact]
    public void Mml_BareV_DoesNotCrash()
    {
        string mmlCode = "MML@V";
        var (mid, _) = _importer.ParseToMidi(mmlCode);
        Assert.NotNull(mid);
    }

    [Fact]
    public void Mml_OutOfBoundsVolume_ThrowsException()
    {
        string mmlCode = "MML@V16 C4;";
        var ex = Assert.Throws<MmlParseException>(() => _importer.ParseToMidi(mmlCode));
        Assert.Contains("Volume must be 0-15", ex.Message);
    }

    [Fact]
    public void Mml_OutOfBoundsTempo_ThrowsException()
    {
        string mmlCode = "MML@T0 C4;";
        var ex = Assert.Throws<MmlParseException>(() => _importer.ParseToMidi(mmlCode));
        Assert.Contains("Tempo must be > 0", ex.Message);
    }

    [Fact]
    public void CanImport_ValidatesCorrectly()
    {
        Assert.True(_importer.CanImport("MML@T120C4;"));
        Assert.True(_importer.CanImport("sample.mml"));
        Assert.False(_importer.CanImport("sample.mid"));
    }

    [Fact]
    public void Mml_AllValidTokens_ParsesSuccessfully()
    {
        string mmlCode = "MML@T120 V10 L8 O4 > C+ D- E F# G A B < R4. C4&;";
        var (mid, meta) = _importer.ParseToMidi(mmlCode);

        Assert.NotNull(mid);
        Assert.True(Convert.ToInt32(meta["notes"]) > 0);
    }

    [Fact]
    public void Mml_NumericNote_ExtractsPitchBounds()
    {
        var meta60 = _importer.ExtractMetadata("MML@N60;");
        Assert.Equal(1, Convert.ToInt32(meta60["total_notes"]));
        Assert.Equal(60, Convert.ToInt32(meta60["min_pitch"]));
        Assert.Equal(60, Convert.ToInt32(meta60["max_pitch"]));

        var meta0 = _importer.ExtractMetadata("MML@N0;");
        Assert.Equal(0, Convert.ToInt32(meta0["min_pitch"]));

        var meta127 = _importer.ExtractMetadata("MML@N127;");
        Assert.Equal(127, Convert.ToInt32(meta127["max_pitch"]));
    }

    [Fact]
    public void Mml_NumericNoteOverflow_ThrowsException()
    {
        var ex = Assert.Throws<MmlParseException>(() => _importer.ParseToMidi("MML@N128;"));
        Assert.Contains("out of bounds", ex.Message);
    }

    [Fact]
    public void Mml_NumericNoteNegative_ThrowsException()
    {
        var ex = Assert.Throws<MmlParseException>(() => _importer.ParseToMidi("MML@N-1;"));
        Assert.Contains("out of bounds", ex.Message);
    }

    [Fact]
    public void Mml_NumericNoteDefaultLength_UsesCurrentDefaultLength()
    {
        var (mid, _) = _importer.ParseToMidi("MML@L16N60;");
        var track = (TrackChunk)mid.Chunks[0];
        var events = track.Events.Where(e => e is NoteOnEvent or NoteOffEvent).ToList();

        Assert.Equal(2, events.Count);
        Assert.Equal(120, events[1].DeltaTime); // L16 = 120 ticks
    }

    [Fact]
    public void Mml_NumericNoteVolume_AppliesVelocityMapping()
    {
        var (mid, _) = _importer.ParseToMidi("MML@V10N60;");
        var track = (TrackChunk)mid.Chunks[0];
        var noteOn = track.Events.OfType<NoteOnEvent>().First(n => n.Velocity > 0);

        int expectedVel = (int)(10 * 127.0 / 15.0);
        Assert.Equal(expectedVel, (int)noteOn.Velocity);
    }

    [Fact]
    public void Mml_Metadata_DurationAndNoteCount()
    {
        var metaDur = _importer.ExtractMetadata("MML@T120L4C;");
        Assert.Equal(0.5, Convert.ToDouble(metaDur["duration"]), precision: 1);

        var metaCount = _importer.ExtractMetadata("MML@CDEFGAB;");
        Assert.Equal(7, Convert.ToInt32(metaCount["total_notes"]));
    }
}
