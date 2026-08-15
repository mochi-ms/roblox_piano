using System.Diagnostics;
using System.Text;

namespace RobloxPiano.Infrastructure.Audio;

public class FfmpegProcessRunner : IFfmpegProcessRunner
{
    public async Task<ProcessExecutionResult> RunProcessAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        Action<string>? onStdOutLine = null,
        Action<string>? onStdErrLine = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return ProcessExecutionResult.Failure(-1, $"실행 파일을 찾을 수 없습니다: {executablePath}");
        }

        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                stdoutBuilder.AppendLine(e.Data);
                onStdOutLine?.Invoke(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                stderrBuilder.AppendLine(e.Data);
                onStdErrLine?.Invoke(e.Data);
            }
        };

        try
        {
            if (!process.Start())
            {
                return ProcessExecutionResult.Failure(-1, "프로세스를 시작할 수 없습니다.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutCts = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : null;
            using var linkedCts = timeoutCts != null
                ? CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token)
                : null;

            var effectiveToken = linkedCts?.Token ?? ct;

            try
            {
                await process.WaitForExitAsync(effectiveToken);
            }
            catch (OperationCanceledException)
            {
                KillProcessSafely(process);

                if (timeoutCts != null && timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    return ProcessExecutionResult.TimedOut($"프로세스가 시간 초과({timeout?.TotalSeconds}초)되었습니다.");
                }

                return ProcessExecutionResult.Cancelled("프로세스가 취소되었습니다.");
            }

            // Ensure stdout/stderr streams are fully flushed
            await process.WaitForExitAsync();

            int exitCode = process.ExitCode;
            string stdout = stdoutBuilder.ToString();
            string stderr = stderrBuilder.ToString();

            return exitCode == 0
                ? ProcessExecutionResult.Success(stdout, stderr)
                : ProcessExecutionResult.Failure(exitCode, stderr, stdout);
        }
        catch (Exception ex)
        {
            KillProcessSafely(process);
            return ProcessExecutionResult.Failure(-1, $"프로세스 실행 중 예외 발생: {ex.Message}");
        }
    }

    private static void KillProcessSafely(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
        }
        catch
        {
            // Ignore failure to kill already exited process
        }
    }
}
