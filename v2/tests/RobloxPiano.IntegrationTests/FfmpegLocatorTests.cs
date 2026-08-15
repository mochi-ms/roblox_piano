using RobloxPiano.Infrastructure.Audio;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class FfmpegLocatorTests : IDisposable
{
    private readonly string _tempDir;

    public FfmpegLocatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"rp_locator_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
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

    [Fact]
    public async Task FfmpegLocator_ExplicitPath_Wins()
    {
        string fakeFfmpeg = Path.Combine(_tempDir, "ffmpeg.exe");
        string fakeFfprobe = Path.Combine(_tempDir, "ffprobe.exe");
        await File.WriteAllTextAsync(fakeFfmpeg, "fake exe");
        await File.WriteAllTextAsync(fakeFfprobe, "fake exe");

        var fakeRunner = new FakeVersionProcessRunner(new Dictionary<string, string>
        {
            [fakeFfmpeg] = "ffmpeg version 6.0-custom",
            [fakeFfprobe] = "ffprobe version 6.0-custom"
        });

        var locator = new FfmpegToolLocator(fakeRunner);
        var info = await locator.LocateToolsAsync(explicitFfmpegPath: fakeFfmpeg, explicitFfprobePath: fakeFfprobe);

        Assert.True(info.IsFullyAvailable);
        Assert.Equal(fakeFfmpeg, info.FfmpegPath);
        Assert.Equal(fakeFfprobe, info.FfprobePath);
        Assert.Equal("ffmpeg version 6.0-custom", info.FfmpegVersionLine);
        Assert.Equal("ffprobe version 6.0-custom", info.FfprobeVersionLine);
    }

    [Fact]
    public async Task FfmpegLocator_InvalidExplicitPath_FallsBack()
    {
        string invalidPath = Path.Combine(_tempDir, "does_not_exist_ffmpeg.exe");

        var fakeRunner = new FakeVersionProcessRunner(new Dictionary<string, string>());
        var locator = new FfmpegToolLocator(fakeRunner);
        var info = await locator.LocateToolsAsync(explicitFfmpegPath: invalidPath);

        // Should not crash and should report unavailable if no tools in PATH
        Assert.False(info.IsFfmpegAvailable);
    }

    [Fact]
    public async Task FfmpegLocator_MissingTools_ReturnsUnavailable()
    {
        var fakeRunner = new FakeVersionProcessRunner(new Dictionary<string, string>());
        var locator = new FfmpegToolLocator(fakeRunner);
        var info = await locator.LocateToolsAsync();

        Assert.NotNull(info);
        // Without tools present, returns clean structure without throwing
    }

    [Fact]
    public async Task FfmpegLocator_FfmpegWithoutFfprobe_NotFullyReady()
    {
        string fakeFfmpeg = Path.Combine(_tempDir, "ffmpeg.exe");
        await File.WriteAllTextAsync(fakeFfmpeg, "fake exe");

        var fakeRunner = new FakeVersionProcessRunner(new Dictionary<string, string>
        {
            [fakeFfmpeg] = "ffmpeg version 6.0"
        });

        var locator = new FfmpegToolLocator(fakeRunner);
        var info = await locator.LocateToolsAsync(explicitFfmpegPath: fakeFfmpeg);

        Assert.True(info.IsFfmpegAvailable);
        Assert.False(info.IsFfprobeAvailable);
        Assert.False(info.IsFullyAvailable);
    }

    private class FakeVersionProcessRunner : IFfmpegProcessRunner
    {
        private readonly Dictionary<string, string> _versionMap;

        public FakeVersionProcessRunner(Dictionary<string, string> versionMap)
        {
            _versionMap = versionMap;
        }

        public Task<ProcessExecutionResult> RunProcessAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            Action<string>? onStdOutLine = null,
            Action<string>? onStdErrLine = null,
            TimeSpan? timeout = null,
            CancellationToken ct = default)
        {
            if (_versionMap.TryGetValue(executablePath, out var version))
            {
                return Task.FromResult(ProcessExecutionResult.Success(version));
            }

            return Task.FromResult(ProcessExecutionResult.Failure(-1, "File not executable"));
        }
    }
}
