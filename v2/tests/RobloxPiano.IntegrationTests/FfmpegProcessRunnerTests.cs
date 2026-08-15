using System.Diagnostics;
using RobloxPiano.Infrastructure.Audio;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class FfmpegProcessRunnerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FfmpegProcessRunner _runner;

    public FfmpegProcessRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"rp_proc_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _runner = new FfmpegProcessRunner();
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
    public async Task ProcessRunner_UsesNoShell()
    {
        // Execute cmd.exe with arguments safely without shell concatenation
        string cmdPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        if (!File.Exists(cmdPath)) return;

        var result = await _runner.RunProcessAsync(
            cmdPath,
            new[] { "/c", "echo", "hello world & echo injected" });

        Assert.True(result.IsSuccess);
        Assert.Contains("hello world & echo injected", result.StandardOutput);
    }

    [Fact]
    public async Task ProcessRunner_CapturesStdoutAndStderr()
    {
        string cmdPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        if (!File.Exists(cmdPath)) return;

        var stdoutLines = new List<string>();
        var result = await _runner.RunProcessAsync(
            cmdPath,
            new[] { "/c", "echo", "stdout line" },
            onStdOutLine: line => stdoutLines.Add(line));

        Assert.True(result.IsSuccess);
        Assert.Contains("stdout line", result.StandardOutput);
        Assert.NotEmpty(stdoutLines);
    }

    [Fact]
    public async Task ProcessRunner_NonZeroExit_ReturnsFailure()
    {
        string cmdPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        if (!File.Exists(cmdPath)) return;

        var result = await _runner.RunProcessAsync(
            cmdPath,
            new[] { "/c", "exit", "42" });

        Assert.False(result.IsSuccess);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public async Task ProcessRunner_Cancellation_KillsProcess()
    {
        string pingPath = Path.Combine(Environment.SystemDirectory, "PING.EXE");
        if (!File.Exists(pingPath)) return;

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(150);

        var result = await _runner.RunProcessAsync(
            pingPath,
            new[] { "127.0.0.1", "-n", "10" },
            ct: cts.Token);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsCancelled);
    }
}
