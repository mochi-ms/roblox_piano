namespace RobloxPiano.Infrastructure.Audio;

public interface IFfmpegProcessRunner
{
    Task<ProcessExecutionResult> RunProcessAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        Action<string>? onStdOutLine = null,
        Action<string>? onStdErrLine = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default);
}
