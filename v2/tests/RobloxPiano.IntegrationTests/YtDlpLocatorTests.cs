using RobloxPiano.Infrastructure.Audio;
using RobloxPiano.Infrastructure.YouTube;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class YtDlpLocatorTests : IDisposable
{
    private readonly string _tempDir;

    public YtDlpLocatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "YtDlpLocatorTests_" + Guid.NewGuid().ToString("N"));
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

    private class MockYtDlpRunner : IYtDlpProcessRunner
    {
        private readonly Func<string, IReadOnlyList<string>, ProcessExecutionResult> _handler;

        public MockYtDlpRunner(Func<string, IReadOnlyList<string>, ProcessExecutionResult> handler)
        {
            _handler = handler;
        }

        public Task<ProcessExecutionResult> RunProcessAsync(string executablePath, IReadOnlyList<string> arguments, Action<string>? onStdOutLine = null, Action<string>? onStdErrLine = null, TimeSpan? timeout = null, CancellationToken ct = default)
        {
            return Task.FromResult(_handler(executablePath, arguments));
        }
    }

    [Fact]
    public async Task YtDlpLocator_ExplicitPath_Wins()
    {
        string fakeExe = Path.Combine(_tempDir, "custom-yt-dlp.exe");
        File.WriteAllText(fakeExe, "#!mock");

        var runner = new MockYtDlpRunner((path, args) =>
        {
            if (path == fakeExe && args.Contains("--version"))
            {
                return ProcessExecutionResult.Success("2024.08.06");
            }
            return ProcessExecutionResult.Failure(1, "error");
        });

        var locator = new YtDlpToolLocator(runner);
        var status = await locator.LocateAsync(fakeExe);

        Assert.True(status.IsAvailable);
        Assert.Equal(fakeExe, status.ExecutablePath);
        Assert.Equal("2024.08.06", status.Version);
    }

    [Fact]
    public async Task YtDlpLocator_Missing_ReturnsUnavailable()
    {
        var runner = new MockYtDlpRunner((_, _) => ProcessExecutionResult.Failure(1, "not found"));
        var locator = new YtDlpToolLocator(runner);

        var status = await locator.LocateAsync(@"C:\non_existent_folder_xyz\yt-dlp.exe");

        Assert.False(status.IsAvailable);
        Assert.Contains("존재하지 않습니다", status.StatusMessage);
    }

    [Fact]
    public async Task YtDlpLocator_VersionCommandFailure_Unavailable()
    {
        string fakeExe = Path.Combine(_tempDir, "broken-yt-dlp.exe");
        File.WriteAllText(fakeExe, "#!mock");

        var runner = new MockYtDlpRunner((_, _) => ProcessExecutionResult.Failure(1, "crash on startup"));
        var locator = new YtDlpToolLocator(runner);

        var status = await locator.LocateAsync(fakeExe);

        Assert.False(status.IsAvailable);
        Assert.Contains("실패", status.StatusMessage);
    }
}
