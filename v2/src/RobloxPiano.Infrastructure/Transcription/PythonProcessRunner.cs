using System.Diagnostics;
using System.IO;
using System.Text;
using RobloxPiano.Infrastructure.Audio;

namespace RobloxPiano.Infrastructure.Transcription;

public class PythonProcessSession : IPythonProcessSession
{
    private readonly Process _process;
    private readonly StreamWriter _stdinWriter;
    private bool _disposed;

    public bool IsRunning => !_disposed && !_process.HasExited;
    public int? ProcessId => !_disposed ? _process.Id : null;

    public PythonProcessSession(
        string executablePath,
        IReadOnlyList<string> arguments,
        Action<string>? onStdOutLine = null,
        Action<string>? onStdErrLine = null,
        string? workingDir = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (!string.IsNullOrWhiteSpace(workingDir) && Directory.Exists(workingDir))
        {
            psi.WorkingDirectory = workingDir;
        }

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        _process = new Process { StartInfo = psi };

        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                onStdOutLine?.Invoke(e.Data);
            }
        };

        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                onStdErrLine?.Invoke(e.Data);
            }
        };

        if (!_process.Start())
        {
            throw new InvalidOperationException($"Python 프로세스를 시작할 수 없습니다: {executablePath}");
        }

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        _stdinWriter = _process.StandardInput;
    }

    public async Task SendLineAsync(string line, CancellationToken ct = default)
    {
        if (_disposed || _process.HasExited)
        {
            throw new InvalidOperationException("Python 프로세스가 이미 종료되었거나 해제되었습니다.");
        }

        await _stdinWriter.WriteLineAsync(line.AsMemory(), ct);
        await _stdinWriter.FlushAsync(ct);
    }

    public void Kill()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(3000);
            }
        }
        catch
        {
            // Ignore failure to kill already exited process
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Kill();
        _process.Dispose();
    }
}

public class PythonProcessRunner : IPythonProcessRunner
{
    private readonly IFfmpegProcessRunner _runner;

    public PythonProcessRunner(IFfmpegProcessRunner? runner = null)
    {
        _runner = runner ?? new FfmpegProcessRunner();
    }

    public Task<ProcessExecutionResult> RunProcessAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        Action<string>? onStdOutLine = null,
        Action<string>? onStdErrLine = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        return _runner.RunProcessAsync(executablePath, arguments, onStdOutLine, onStdErrLine, timeout, ct);
    }

    public IPythonProcessSession StartSession(
        string executablePath,
        IReadOnlyList<string> arguments,
        Action<string>? onStdOutLine = null,
        Action<string>? onStdErrLine = null,
        string? workingDir = null)
    {
        return new PythonProcessSession(executablePath, arguments, onStdOutLine, onStdErrLine, workingDir);
    }
}
