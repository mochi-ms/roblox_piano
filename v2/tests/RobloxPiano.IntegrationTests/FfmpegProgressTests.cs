using RobloxPiano.Infrastructure.Audio;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class FfmpegProgressTests
{
    [Fact]
    public void FfmpegProgress_ParsesOutTime()
    {
        var parser = new FfmpegProgressParser(totalDurationSeconds: 100.0);

        // out_time_us = 50,000,000 us = 50.0s => progress = 0.50
        double? progress = parser.ParseLine("out_time_us=50000000");

        Assert.NotNull(progress);
        Assert.Equal(0.50, progress.Value, precision: 2);
    }

    [Fact]
    public void FfmpegProgress_ClampsAboveOne()
    {
        var parser = new FfmpegProgressParser(totalDurationSeconds: 60.0);

        // out_time_us = 120,000,000 us = 120s => progress = 1.0 clamped
        double? progress = parser.ParseLine("out_time_us=120000000");

        Assert.NotNull(progress);
        Assert.Equal(1.0, progress.Value);
    }

    [Fact]
    public void FfmpegProgress_IgnoresMalformedLines()
    {
        var parser = new FfmpegProgressParser(totalDurationSeconds: 60.0);

        Assert.Null(parser.ParseLine(null));
        Assert.Null(parser.ParseLine(""));
        Assert.Null(parser.ParseLine("    "));
        Assert.Null(parser.ParseLine("frame=123"));
        Assert.Null(parser.ParseLine("fps=0.00"));
        Assert.Null(parser.ParseLine("out_time_us=invalid_number"));
        Assert.Null(parser.ParseLine("malformed_without_equals"));
    }

    [Fact]
    public void FfmpegProgress_ProgressEnd_ReturnsOne()
    {
        var parser = new FfmpegProgressParser(totalDurationSeconds: 120.0);

        double? progress = parser.ParseLine("progress=end");

        Assert.NotNull(progress);
        Assert.Equal(1.0, progress.Value);
    }
}
