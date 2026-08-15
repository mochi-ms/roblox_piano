namespace RobloxPiano.Infrastructure.Audio;

public record ProcessExecutionResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool IsSuccess,
    bool IsCancelled = false,
    bool IsTimedOut = false
)
{
    public static ProcessExecutionResult Success(string stdout, string stderr = "") =>
        new(0, stdout, stderr, true);

    public static ProcessExecutionResult Failure(int exitCode, string stderr, string stdout = "") =>
        new(exitCode, stdout, stderr, false);

    public static ProcessExecutionResult Cancelled(string stderr = "Process cancelled.") =>
        new(-1, string.Empty, stderr, false, IsCancelled: true);

    public static ProcessExecutionResult TimedOut(string stderr = "Process timed out.") =>
        new(-2, string.Empty, stderr, false, IsTimedOut: true);
}
