using System.IO;
using System.Text.Json;
using RobloxPiano.Core.Importing;
using RobloxPiano.Core.Transcription;
using RobloxPiano.Infrastructure.Audio;
using RobloxPiano.Infrastructure.Transcription;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class TranscriptionResultValidationTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly TranscriptionWorkspaceService _workspace;

    public TranscriptionResultValidationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "rp_val_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _workspace = new TranscriptionWorkspaceService(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch { }
    }

    private class MockPythonLocator : IPythonLocator
    {
        public Task<(string? PythonPath, string? VersionLine, bool IsValidPython311)> LocatePythonAsync(string? explicitPath = null, CancellationToken ct = default)
        {
            return Task.FromResult<(string?, string?, bool)>((@"C:\mock\python.exe", "Python 3.11.2", true));
        }
    }

    private class MockPythonSession : IPythonProcessSession
    {
        private readonly Action<string>? _onStdOut;
        private readonly TaskCompletionSource<int> _completionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsRunning { get; private set; } = true;
        public int? ProcessId => 9999;
        public Task<int> Completion => _completionTcs.Task;
        private readonly Func<string, (string Type, string ResponseJson)>? _responder;

        public MockPythonSession(Action<string>? onStdOut, Func<string, (string Type, string ResponseJson)>? responder = null)
        {
            _onStdOut = onStdOut;
            _responder = responder;
            _onStdOut?.Invoke("{\"type\":\"hello\",\"protocol\":1,\"request_id\":\"startup\",\"worker_version\":\"1.0.0\",\"python_version\":\"3.11.2\",\"basic_pitch_version\":\"0.4.0\",\"engine_available\":true,\"status_message\":\"정상\"}");
        }

        public Task SendLineAsync(string line, CancellationToken ct = default)
        {
            if (_responder != null)
            {
                var (type, resp) = _responder(line);
                if (!string.IsNullOrEmpty(resp))
                {
                    _onStdOut?.Invoke(resp);
                }
            }
            return Task.CompletedTask;
        }

        public void Kill()
        {
            IsRunning = false;
            _completionTcs.TrySetResult(-1);
        }

        public void Dispose() => Kill();
    }

    private class MockProcessRunner : IPythonProcessRunner
    {
        private readonly Func<string, (string Type, string ResponseJson)>? _responder;
        public MockProcessRunner(Func<string, (string Type, string ResponseJson)>? responder = null) => _responder = responder;

        public Task<ProcessExecutionResult> RunProcessAsync(string executablePath, IReadOnlyList<string> arguments, Action<string>? onStdOutLine = null, Action<string>? onStdErrLine = null, TimeSpan? timeout = null, CancellationToken ct = default)
        {
            return Task.FromResult(ProcessExecutionResult.Success("Python 3.11.2"));
        }

        public IPythonProcessSession StartSession(string executablePath, IReadOnlyList<string> arguments, Action<string>? onStdOutLine = null, Action<string>? onStdErrLine = null, string? workingDir = null)
        {
            return new MockPythonSession(onStdOutLine, _responder);
        }
    }

    [Fact]
    public async Task TranscribeAsync_ZeroNoteMidi_FailsValidation()
    {
        string jobId = "job_zero_note";
        string inputAudio = Path.Combine(_tempRoot, "audio.wav");
        File.WriteAllBytes(inputAudio, new byte[] { 1, 2, 3 });

        string finalMidi = _workspace.GetFinalMidiPath(jobId);

        var runner = new MockProcessRunner(line =>
        {
            using var doc = JsonDocument.Parse(line);
            string reqId = doc.RootElement.GetProperty("request_id").GetString()!;
            string jId = doc.RootElement.GetProperty("job_id").GetString()!;

            // Write valid MIDI header with zero note events
            byte[] zeroNoteMidi = new byte[]
            {
                0x4D, 0x54, 0x68, 0x64,
                0x00, 0x00, 0x00, 0x06,
                0x00, 0x00,
                0x00, 0x01,
                0x01, 0xE0,
                0x4D, 0x54, 0x72, 0x6B,
                0x00, 0x00, 0x00, 0x04,
                0x00, 0xFF, 0x2F, 0x00
            };
            File.WriteAllBytes(finalMidi, zeroNoteMidi);

            string resp = $"{{\"type\":\"result\",\"protocol\":1,\"request_id\":\"{reqId}\",\"job_id\":\"{jId}\",\"midi_path\":\"{finalMidi.Replace("\\", "\\\\")}\",\"note_count\":0,\"duration_seconds\":0.0,\"min_pitch\":null,\"max_pitch\":null,\"runtime_seconds\":0.5,\"engine_version\":\"0.4.0\"}}";
            return ("result", resp);
        });

        using var engine = new PythonBasicPitchTranscriptionEngine(
            pythonLocator: new MockPythonLocator(),
            processRunner: runner,
            workspaceService: _workspace,
            explicitWorkerScriptPath: Path.Combine(_tempRoot, "mock_worker.py")
        );

        File.WriteAllText(Path.Combine(_tempRoot, "mock_worker.py"), "# mock");

        var req = new TranscriptionRequest(jobId, inputAudio);
        var result = await engine.TranscribeAsync(req);

        Assert.False(result.Success);
        Assert.Equal("VALIDATION_FAILED", result.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public async Task TranscribeAsync_CorruptMidi_FailsValidation()
    {
        string jobId = "job_corrupt_midi";
        string inputAudio = Path.Combine(_tempRoot, "audio.wav");
        File.WriteAllBytes(inputAudio, new byte[] { 1, 2, 3 });

        string finalMidi = _workspace.GetFinalMidiPath(jobId);

        var runner = new MockProcessRunner(line =>
        {
            using var doc = JsonDocument.Parse(line);
            string reqId = doc.RootElement.GetProperty("request_id").GetString()!;
            string jId = doc.RootElement.GetProperty("job_id").GetString()!;

            // Write garbage
            File.WriteAllBytes(finalMidi, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

            string resp = $"{{\"type\":\"result\",\"protocol\":1,\"request_id\":\"{reqId}\",\"job_id\":\"{jId}\",\"midi_path\":\"{finalMidi.Replace("\\", "\\\\")}\",\"note_count\":5,\"duration_seconds\":1.0,\"min_pitch\":60,\"max_pitch\":64,\"runtime_seconds\":0.5,\"engine_version\":\"0.4.0\"}}";
            return ("result", resp);
        });

        using var engine = new PythonBasicPitchTranscriptionEngine(
            pythonLocator: new MockPythonLocator(),
            processRunner: runner,
            workspaceService: _workspace,
            explicitWorkerScriptPath: Path.Combine(_tempRoot, "mock_worker.py")
        );

        File.WriteAllText(Path.Combine(_tempRoot, "mock_worker.py"), "# mock");

        var req = new TranscriptionRequest(jobId, inputAudio);
        var result = await engine.TranscribeAsync(req);

        Assert.False(result.Success);
        Assert.Equal("VALIDATION_FAILED", result.ErrorCode);
    }
}
