using RobloxPiano.Core.Importers;
using Xunit;

namespace RobloxPiano.Core.Tests;

public class MmlDialectTests
{
    private readonly MmlImporter _importer = new();

    [Fact]
    public void Mml_Lowercase_BehavesIdenticallyToUppercase()
    {
        string mmlUpper = "MML@T120V15L8O4CDEFG;";
        string mmlLower = "mml@t120v15l8o4cdefg;";

        var metaU = _importer.ExtractMetadata(mmlUpper);
        var metaL = _importer.ExtractMetadata(mmlLower);

        Assert.Equal(5, Convert.ToInt32(metaU["notes"]));
        Assert.Equal(5, Convert.ToInt32(metaL["notes"]));
        Assert.Equal(Convert.ToDouble(metaU["duration"]), Convert.ToDouble(metaL["duration"]), precision: 3);
        Assert.Equal(120, Convert.ToInt32(metaU["bpm"]));
        Assert.Equal(120, Convert.ToInt32(metaL["bpm"]));
    }

    [Fact]
    public void Mml_DottedDefaultLength_CalculatesCorrectDuration()
    {
        var metaL4 = _importer.ExtractMetadata("MML@T120L4C;");
        var metaL4Dot = _importer.ExtractMetadata("MML@T120L4.C;");

        Assert.Equal(1, Convert.ToInt32(metaL4["notes"]));
        Assert.Equal(1, Convert.ToInt32(metaL4Dot["notes"]));
        Assert.Equal(0.50, Convert.ToDouble(metaL4["duration"]), precision: 2);
        Assert.Equal(0.75, Convert.ToDouble(metaL4Dot["duration"]), precision: 2);
    }

    [Fact]
    public void Mml_StandaloneTie_CombinesNotes()
    {
        var metaTied = _importer.ExtractMetadata("MML@T120L4C&C;");
        var metaSeparate = _importer.ExtractMetadata("MML@T120L4C C;");

        Assert.Equal(1, Convert.ToInt32(metaTied["notes"]));
        Assert.Equal(1.0, Convert.ToDouble(metaTied["duration"]), precision: 2);

        Assert.Equal(2, Convert.ToInt32(metaSeparate["notes"]));
        Assert.Equal(1.0, Convert.ToDouble(metaSeparate["duration"]), precision: 2);
    }

    [Fact]
    public void Mml_NumericNoteTieAndLength_CombinesCorrectly()
    {
        var meta = _importer.ExtractMetadata("MML@T120L4N60L4.&N60;");
        Assert.Equal(1, Convert.ToInt32(meta["notes"]));
        // 120 BPM: quarter (0.5) + dotted quarter (0.75) = 1.25s
        Assert.Equal(1.25, Convert.ToDouble(meta["duration"]), precision: 2);
    }

    [Fact]
    public void Mml_MultiTrackWithDuplicateTempo_ParsesAllTracks()
    {
        string mml = "MML@T120L4CDEF,T120L4O3CDEF,T120L4O2CDEF;";
        var meta = _importer.ExtractMetadata(mml);

        Assert.Equal(3, Convert.ToInt32(meta["tracks"]));
        Assert.Equal(12, Convert.ToInt32(meta["notes"]));
        Assert.Equal(120, Convert.ToInt32(meta["bpm"]));
    }

    [Fact]
    public void Mml_64thNotes_ParsesDuration()
    {
        string mml = "MML@T120L64CDEF GAB>C;";
        var meta = _importer.ExtractMetadata(mml);

        Assert.Equal(8, Convert.ToInt32(meta["notes"]));
        Assert.True(Convert.ToDouble(meta["duration"]) > 0);
    }
}
