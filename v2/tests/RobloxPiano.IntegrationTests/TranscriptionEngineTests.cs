using System.IO;
using System.Text.Json;
using RobloxPiano.Core.Importing;
using RobloxPiano.Core.Music;
using RobloxPiano.Core.Transcription;
using RobloxPiano.Infrastructure.Audio;
using RobloxPiano.Infrastructure.Transcription;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class TranscriptionEngineTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly TranscriptionWorkspaceService _workspace;

    public TranscriptionEngineTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "rp_engine_test_" + Guid.NewGuid().ToString("N"));
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

    private static byte[] CreateMinimalMidiBytes()
    {
        return new byte[]
        {
            0x4D, 0x54, 0x68, 0x64,
            0x00, 0x00, 0x00, 0x06,
            0x00, 0x00,
            0x00, 0x01,
            0x01, 0xE0,
            0x4D, 0x54, 0x72, 0x6B,
            0x00, 0x00, 0x00, 0x16,
            0x00, 0x90, 0x3C, 0x60,
            0x83, 0x60, 0x80, 0x3C, 0x40,
            0x00, 0x90, 0x40, 0x60,
            0x83, 0x60, 0x80, 0x40, 0x40,
            0x00, 0xFF, 0x2F, 0x00
        };
    }

    private class MockPythonLocator : IPythonLocator
    {
        private readonly bool _available;
        public MockPythonLocator(bool available = true) => _available = available;

        public Task<(string? PythonPath, string? VersionLine, bool IsValidPython311)> LocatePythonAsync(string? explicitPath = null, CancellationToken ct = default)
        {
            if (_available)
            {
                return Task.FromResult<(string?, string?, bool)>((@"C:\mock\python.exe", "Python 3.11.2", true));
            }
            return Task.FromResult<(string?, string?, bool)>((null, null, false));
        }
    }

    private class MockPythonSession : IPythonProcessSession
    {
        private readonly Action<string>? _onStdOut;
        public bool IsRunning { get; private set; } = true;
        public int? ProcessId => 9999;
        public bool KillCalled { get; private set; }
        public string? LastReceivedLine { get; private set; }
        private readonly Func<string, (string Type, string ResponseJson)>? _responder;

        public MockPythonSession(Action<string>? onStdOut, Func<string, (string Type, string ResponseJson)>? responder = null)
        {
            _onStdOut = onStdOut;
            _responder = responder;

            // Emit hello handshake
            _onStdOut?.Invoke("{\"type\":\"hello\",\"protocol\":1,\"request_id\":\"startup\",\"worker_version\":\"1.0.0\",\"python_version\":\"3.11.2\",\"basic_pitch_version\":\"0.4.0\"}");
        }

        public Task SendLineAsync(string line, CancellationToken ct = default)
        {
            LastReceivedLine = line;
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
            KillCalled = true;
            IsRunning = false;
        }

        public void Dispose() => Kill();
    }

    private class MockProcessRunner : IPythonProcessRunner
    {
        public MockPythonSession? CurrentSession { get; private set; }
        private readonly Func<string, (string Type, string ResponseJson)>? _responder;

        public MockProcessRunner(Func<string, (string Type, string ResponseJson)>? responder = null)
        {
            _responder = responder;
        }

        public Task<ProcessExecutionResult> RunProcessAsync(string executablePath, IReadOnlyList<string> arguments, Action<string>? onStdOutLine = null, Action<string>? onStdErrLine = null, TimeSpan? timeout = null, CancellationToken ct = default)
        {
            return Task.FromResult(ProcessExecutionResult.Success("Python 3.11.2"));
        }

        public IPythonProcessSession StartSession(string executablePath, IReadOnlyList<string> arguments, Action<string>? onStdOutLine = null, Action<string>? onStdErrLine = null, string? workingDir = null)
        {
            CurrentSession = new MockPythonSession(onStdOutLine, _responder);
            return CurrentSession;
        }
    }

    [Fact]
    public async Task TranscribeAsync_ValidAudio_ReturnsSuccessfulResult()
    {
        string jobId = "job_success_01";
        string inputAudio = Path.Combine(_tempRoot, "audio.wav");
        File.WriteAllBytes(inputAudio, new byte[] { 1, 2, 3 });

        string finalMidi = _workspace.GetFinalMidiPath(jobId);

        var runner = new MockProcessRunner(line =>
        {
            using var doc = JsonDocument.Parse(line);
            string reqId = doc.RootElement.GetProperty("request_id").GetString()!;
            string jId = doc.RootElement.GetProperty("job_id").GetString()!;

            // Write minimal MIDI
            File.WriteAllBytes(finalMidi, CreateMinimalMidiBytes());

            string resp = $"{{\"type\":\"result\",\"protocol\":1,\"request_id\":\"{reqId}\",\"job_id\":\"{jId}\",\"midi_path\":\"{finalMidi.Replace("\\", "\\\\")}\",\"note_count\":2,\"duration_seconds\":2.0,\"min_pitch\":60,\"max_pitch\":64,\"runtime_seconds\":1.2,\"engine_version\":\"0.4.0\"}}";
            return ("result", resp);
        });

        using var engine = new PythonBasicPitchTranscriptionEngine(
            pythonLocator: new MockPythonLocator(true),
            processRunner: runner,
            workspaceService: _workspace,
            explicitWorkerScriptPath: Path.Combine(_tempRoot, "mock_worker.py")
        );

        File.WriteAllText(Path.Combine(_tempRoot, "mock_worker.py"), "# mock");

        var req = new TranscriptionRequest(jobId, inputAudio);
        var result = await engine.TranscribeAsync(req);

        Assert.True(result.Success);
        Assert.Equal(jobId, result.JobId);
        Assert.Equal(finalMidi, result.GeneratedMidiPath);
        Assert.NotNull(result.Timeline);
        Assert.Equal(2, result.NoteCount);
        Assert.Equal(2, result.PlayableNoteCount);
    }

    [Fact]
    public async Task TranscribeAsync_WorkerError_ReturnsFailedResultAndCleansWorkspace()
    {
        string jobId = "job_error_01";
        string inputAudio = Path.Combine(_tempRoot, "audio.wav");
        File.WriteAllBytes(inputAudio, new byte[] { 1, 2, 3 });

        var runner = new MockProcessRunner(line =>
        {
            using var doc = JsonDocument.Parse(line);
            string reqId = doc.RootElement.GetProperty("request_id").GetString()!;
            string jId = doc.RootElement.GetProperty("job_id").GetString()!;

            string resp = $"{{\"type\":\"error\",\"protocol\":1,\"request_id\":\"{reqId}\",\"job_id\":\"{jId}\",\"error_code\":\"CUDA_OOM\",\"error_message\":\"Out of memory\"}}";
            return ("error", resp);
        });

        using var engine = new PythonBasicPitchTranscriptionEngine(
            pythonLocator: new MockPythonLocator(true),
            processRunner: runner,
            workspaceService: _workspace,
            explicitWorkerScriptPath: Path.Combine(_tempRoot, "mock_worker.py")
        );

        File.WriteAllText(Path.Combine(_tempRoot, "mock_worker.py"), "# mock");

        var req = new TranscriptionRequest(jobId, inputAudio);
        var result = await engine.TranscribeAsync(req);

        Assert.False(result.Success);
        Assert.Equal("CUDA_OOM", result.ErrorCode);
        Assert.Equal("Out of memory", result.ErrorMessage);
        Assert.False(Directory.Exists(_workspace.GetSafeJobDirectoryPath(jobId, createDirectory: false)));
    }

    [Fact]
    public async Task TranscribeAsync_Cancellation_KillsWorkerAndCleansWorkspace()
    {
        string jobId = "job_cancel_01";
        string inputAudio = Path.Combine(_tempRoot, "audio.wav");
        File.WriteAllBytes(inputAudio, new byte[] { 1, 2, 3 });

        using var cts = new CancellationTokenSource();

        var runner = new MockProcessRunner(line =>
        {
            // Simulate delay and cancel
            cts.Cancel();
            return ("none", "");
        });

        using var engine = new PythonBasicPitchTranscriptionEngine(
            pythonLocator: new MockPythonLocator(true),
            processRunner: runner,
            workspaceService: _workspace,
            explicitWorkerScriptPath: Path.Combine(_tempRoot, "mock_worker.py")
        );

        File.WriteAllText(Path.Combine(_tempRoot, "mock_worker.py"), "# mock");

        var req = new TranscriptionRequest(jobId, inputAudio);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await engine.TranscribeAsync(req, ct: cts.Token);
        });

        Assert.True(runner.CurrentSession?.KillCalled);
        Assert.False(Directory.Exists(_workspace.GetSafeJobDirectoryPath(jobId, createDirectory: false)));
    }
}
