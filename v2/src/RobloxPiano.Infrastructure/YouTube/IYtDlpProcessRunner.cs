using RobloxPiano.Infrastructure.Audio;

namespace RobloxPiano.Infrastructure.YouTube;

public interface IYtDlpProcessRunner
{
    Task<ProcessExecutionResult> RunProcessAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        Action<string>? onStdOutLine = null,
        Action<string>? onStdErrLine = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default);
}
