using RobloxPiano.Core.Importers;
using Xunit;

namespace RobloxPiano.Core.Tests;

public class PythonCrossCheckTests
{
    private readonly MmlImporter _importer = new();

    [Fact]
    public void CrossCheck_1_N58L8GG()
    {
        string mml = "MML@T150L16N58L8GG;";
        var meta = _importer.ExtractMetadata(mml);

        Assert.Equal(1, Convert.ToInt32(meta["tracks"]));
        Assert.Equal(150, Convert.ToInt32(meta["bpm"]));
        Assert.Equal(3, Convert.ToInt32(meta["notes"]));
        Assert.Equal(58, Convert.ToInt32(meta["min_pitch"]));
        Assert.Equal(67, Convert.ToInt32(meta["max_pitch"]));

        // 600 ticks at 150 BPM (480 ticks = 60/150 = 0.4s -> 600 ticks = 0.5s)
        Assert.Equal(0.50, Convert.ToDouble(meta["duration"]), precision: 2);
    }

    [Fact]
    public void CrossCheck_2_CL16D()
    {
        string mml = "MML@T120L8CL16D;";
        var meta = _importer.ExtractMetadata(mml);

        Assert.Equal(1, Convert.ToInt32(meta["tracks"]));
        Assert.Equal(120, Convert.ToInt32(meta["bpm"]));
        Assert.Equal(2, Convert.ToInt32(meta["notes"]));
        Assert.Equal(60, Convert.ToInt32(meta["min_pitch"]));
        Assert.Equal(62, Convert.ToInt32(meta["max_pitch"]));

        // C(240 ticks) + D(120 ticks) = 360 ticks = 0.375s
        Assert.Equal(0.375, Convert.ToDouble(meta["duration"]), precision: 3);
    }

    [Fact]
    public void CrossCheck_3_C16D()
    {
        string mml = "MML@T120L8C16D;";
        var meta = _importer.ExtractMetadata(mml);

        Assert.Equal(1, Convert.ToInt32(meta["tracks"]));
        Assert.Equal(120, Convert.ToInt32(meta["bpm"]));
        Assert.Equal(2, Convert.ToInt32(meta["notes"]));

        // C16(120 ticks) + D(240 ticks) = 360 ticks = 0.375s
        Assert.Equal(0.375, Convert.ToDouble(meta["duration"]), precision: 3);
    }

    [Fact]
    public void CrossCheck_4_TiedC()
    {
        string mml = "MML@T120L8C&C;";
        var meta = _importer.ExtractMetadata(mml);

        Assert.Equal(1, Convert.ToInt32(meta["tracks"]));
        Assert.Equal(1, Convert.ToInt32(meta["notes"]));
        // 480 ticks = 0.50s
        Assert.Equal(0.50, Convert.ToDouble(meta["duration"]), precision: 2);
    }

    [Fact]
    public void CrossCheck_5_MultipleTransitions()
    {
        string mml = "MML@T120L4.CL8.DL16.E;";
        var meta = _importer.ExtractMetadata(mml);

        Assert.Equal(1, Convert.ToInt32(meta["tracks"]));
        Assert.Equal(3, Convert.ToInt32(meta["notes"]));
        // 720 + 360 + 180 = 1260 ticks = 1260 / 960 = 1.3125s
        Assert.Equal(1.3125, Convert.ToDouble(meta["duration"]), precision: 3);
    }

    [Fact]
    public void CrossCheck_6_NumericNoteTie()
    {
        string mml = "MML@T120L4N60L4.&N60;";
        var meta = _importer.ExtractMetadata(mml);

        Assert.Equal(1, Convert.ToInt32(meta["tracks"]));
        Assert.Equal(1, Convert.ToInt32(meta["notes"]));
        Assert.Equal(60, Convert.ToInt32(meta["min_pitch"]));
        Assert.Equal(60, Convert.ToInt32(meta["max_pitch"]));
        // 480 + 720 = 1200 ticks = 1.25s
        Assert.Equal(1.25, Convert.ToDouble(meta["duration"]), precision: 2);
    }

    [Fact]
    public void CrossCheck_7_ThreeTrackSample()
    {
        string mml = "MML@T120L4CDEF,T120L4O3CDEF,T120L4O2CDEF;";
        var meta = _importer.ExtractMetadata(mml);

        Assert.Equal(3, Convert.ToInt32(meta["tracks"]));
        Assert.Equal(120, Convert.ToInt32(meta["bpm"]));
        Assert.Equal(12, Convert.ToInt32(meta["notes"]));
        Assert.Equal(36, Convert.ToInt32(meta["min_pitch"])); // O2 C = (2+1)*12 = 36
        Assert.Equal(65, Convert.ToInt32(meta["max_pitch"])); // O4 F = (4+1)*12 + 5 = 65
        Assert.Equal(2.0, Convert.ToDouble(meta["duration"]), precision: 2);
    }
}
