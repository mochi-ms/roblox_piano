using System.Diagnostics;
using RobloxPiano.Infrastructure.Audio;

namespace RobloxPiano.Infrastructure.Transcription;

public interface IPythonProcessSession : IDisposable
{
    bool IsRunning { get; }
    int? ProcessId { get; }
    Task<int> Completion { get; }
    Task SendLineAsync(string line, CancellationToken ct = default);
    void Kill();
}

public interface IPythonProcessRunner
{
    Task<ProcessExecutionResult> RunProcessAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        Action<string>? onStdOutLine = null,
        Action<string>? onStdErrLine = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default);

    IPythonProcessSession StartSession(
        string executablePath,
        IReadOnlyList<string> arguments,
        Action<string>? onStdOutLine = null,
        Action<string>? onStdErrLine = null,
        string? workingDir = null);
}
