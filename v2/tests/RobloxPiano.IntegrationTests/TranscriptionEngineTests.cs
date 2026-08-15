using System.IO;
using System.Text.Json;
using RobloxPiano.Core.Importing;
using RobloxPiano.Core.Music;
using RobloxPiano.Core.Piano;
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

    private static byte[] CreateMinimalMidiBytes(int pitch1 = 60, int pitch2 = 64)
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
            0x00, 0x90, (byte)pitch1, 0x60,
            0x83, 0x60, 0x80, (byte)pitch1, 0x40,
            0x00, 0x90, (byte)pitch2, 0x60,
            0x83, 0x60, 0x80, (byte)pitch2, 0x40,
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

        public MockPythonSession(
            Action<string>? onStdOut,
            Func<string, (string Type, string ResponseJson)>? responder = null,
            string? customHelloJson = null)
        {
            _onStdOut = onStdOut;
            _responder = responder;

            // Emit hello handshake
            string hello = customHelloJson ?? "{\"type\":\"hello\",\"protocol\":1,\"request_id\":\"startup\",\"worker_version\":\"1.0.0\",\"python_version\":\"3.11.2\",\"basic_pitch_version\":\"0.4.0\",\"engine_available\":true,\"status_message\":\"정상\"}";
            _onStdOut?.Invoke(hello);
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
        private readonly string? _customHelloJson;

        public MockProcessRunner(Func<string, (string Type, string ResponseJson)>? responder = null, string? customHelloJson = null)
        {
            _responder = responder;
            _customHelloJson = customHelloJson;
        }

        public Task<ProcessExecutionResult> RunProcessAsync(string executablePath, IReadOnlyList<string> arguments, Action<string>? onStdOutLine = null, Action<string>? onStdErrLine = null, TimeSpan? timeout = null, CancellationToken ct = default)
        {
            return Task.FromResult(ProcessExecutionResult.Success("Python 3.11.2"));
        }

        public IPythonProcessSession StartSession(string executablePath, IReadOnlyList<string> arguments, Action<string>? onStdOutLine = null, Action<string>? onStdErrLine = null, string? workingDir = null)
        {
            CurrentSession = new MockPythonSession(onStdOutLine, _responder, _customHelloJson);
            return CurrentSession;
        }
    }

    [Fact]
    public async Task CheckAvailability_HelloAvailable_ReturnsAvailable()
    {
        string workerScript = Path.Combine(_tempRoot, "mock_worker.py");
        File.WriteAllText(workerScript, "# mock");

        var runner = new MockProcessRunner();
        using var engine = new PythonBasicPitchTranscriptionEngine(
            pythonLocator: new MockPythonLocator(true),
            processRunner: runner,
            explicitWorkerScriptPath: workerScript
        );

        var status = await engine.CheckAvailabilityAsync();
        Assert.True(status.IsAvailable);
        Assert.Equal("0.4.0", status.BasicPitchVersion);
    }

    [Fact]
    public async Task CheckAvailability_HelloEngineUnavailable_ReturnsUnavailable()
    {
        string workerScript = Path.Combine(_tempRoot, "mock_worker.py");
        File.WriteAllText(workerScript, "# mock");

        string helloUnavailable = "{\"type\":\"hello\",\"protocol\":1,\"request_id\":\"startup\",\"worker_version\":\"1.0.0\",\"python_version\":\"3.11.2\",\"basic_pitch_version\":\"none\",\"engine_available\":false,\"status_message\":\"Basic Pitch 패키지 없음\"}";
        var runner = new MockProcessRunner(customHelloJson: helloUnavailable);

        using var engine = new PythonBasicPitchTranscriptionEngine(
            pythonLocator: new MockPythonLocator(true),
            processRunner: runner,
            explicitWorkerScriptPath: workerScript
        );

        var status = await engine.CheckAvailabilityAsync();
        Assert.False(status.IsAvailable);
        Assert.Contains("Basic Pitch", status.StatusMessage);
    }

    [Fact]
    public async Task CheckAvailability_WrongBasicPitchVersion_ReturnsUnavailable()
    {
        string workerScript = Path.Combine(_tempRoot, "mock_worker.py");
        File.WriteAllText(workerScript, "# mock");

        string helloWrongVer = "{\"type\":\"hello\",\"protocol\":1,\"request_id\":\"startup\",\"worker_version\":\"1.0.0\",\"python_version\":\"3.11.2\",\"basic_pitch_version\":\"0.5.0\",\"engine_available\":false,\"status_message\":\"Basic Pitch 0.4.0이 필요하지만 현재 0.5.0가 설치되어 있습니다.\"}";
        var runner = new MockProcessRunner(customHelloJson: helloWrongVer);

        using var engine = new PythonBasicPitchTranscriptionEngine(
            pythonLocator: new MockPythonLocator(true),
            processRunner: runner,
            explicitWorkerScriptPath: workerScript
        );

        var status = await engine.CheckAvailabilityAsync();
        Assert.False(status.IsAvailable);
        Assert.Contains("0.4.0", status.StatusMessage);
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
    public async Task TranscribeAsync_UntrustedMidiPathDifferentLocation_Rejected()
    {
        string jobId = "job_untrusted_01";
        string inputAudio = Path.Combine(_tempRoot, "audio.wav");
        File.WriteAllBytes(inputAudio, new byte[] { 1, 2, 3 });

        string arbitraryPath = Path.Combine(_tempRoot, "outside_song.mid");
        File.WriteAllBytes(arbitraryPath, CreateMinimalMidiBytes());

        var runner = new MockProcessRunner(line =>
        {
            using var doc = JsonDocument.Parse(line);
            string reqId = doc.RootElement.GetProperty("request_id").GetString()!;
            string jId = doc.RootElement.GetProperty("job_id").GetString()!;

            string resp = $"{{\"type\":\"result\",\"protocol\":1,\"request_id\":\"{reqId}\",\"job_id\":\"{jId}\",\"midi_path\":\"{arbitraryPath.Replace("\\", "\\\\")}\",\"note_count\":2,\"duration_seconds\":2.0,\"min_pitch\":60,\"max_pitch\":64,\"runtime_seconds\":1.2,\"engine_version\":\"0.4.0\"}}";
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

        Assert.False(result.Success);
        Assert.Equal("UNTRUSTED_MIDI_PATH", result.ErrorCode);
    }

    [Fact]
    public async Task TranscribeAsync_UntrustedMidiPathOtherJob_Rejected()
    {
        string jobId = "job_main_01";
        string otherJobId = "job_other_02";
        string inputAudio = Path.Combine(_tempRoot, "audio.wav");
        File.WriteAllBytes(inputAudio, new byte[] { 1, 2, 3 });

        string otherJobMidi = _workspace.GetFinalMidiPath(otherJobId);
        File.WriteAllBytes(otherJobMidi, CreateMinimalMidiBytes());

        var runner = new MockProcessRunner(line =>
        {
            using var doc = JsonDocument.Parse(line);
            string reqId = doc.RootElement.GetProperty("request_id").GetString()!;
            string jId = doc.RootElement.GetProperty("job_id").GetString()!;

            string resp = $"{{\"type\":\"result\",\"protocol\":1,\"request_id\":\"{reqId}\",\"job_id\":\"{jId}\",\"midi_path\":\"{otherJobMidi.Replace("\\", "\\\\")}\",\"note_count\":2,\"duration_seconds\":2.0,\"min_pitch\":60,\"max_pitch\":64,\"runtime_seconds\":1.2,\"engine_version\":\"0.4.0\"}}";
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

        Assert.False(result.Success);
        Assert.Equal("UNTRUSTED_MIDI_PATH", result.ErrorCode);
    }

    [Fact]
    public async Task TranscribeAsync_ProfileAwareDiagnostics_88KeyVs61Key()
    {
        string jobId88 = "job_profile_88";
        string jobId61 = "job_profile_61";
        string inputAudio = Path.Combine(_tempRoot, "audio.wav");
        File.WriteAllBytes(inputAudio, new byte[] { 1, 2, 3 });

        // Note at pitch 21 (A0) and pitch 108 (C8)
        byte[] midi88 = CreateMinimalMidiBytes(pitch1: 21, pitch2: 108);

        var runner = new MockProcessRunner(line =>
        {
            using var doc = JsonDocument.Parse(line);
            string reqId = doc.RootElement.GetProperty("request_id").GetString()!;
            string jId = doc.RootElement.GetProperty("job_id").GetString()!;

            string mPath = _workspace.GetFinalMidiPath(jId);
            File.WriteAllBytes(mPath, midi88);

            string resp = $"{{\"type\":\"result\",\"protocol\":1,\"request_id\":\"{reqId}\",\"job_id\":\"{jId}\",\"midi_path\":\"{mPath.Replace("\\", "\\\\")}\",\"note_count\":2,\"duration_seconds\":2.0,\"min_pitch\":21,\"max_pitch\":108,\"runtime_seconds\":1.2,\"engine_version\":\"0.4.0\"}}";
            return ("result", resp);
        });

        using var engine = new PythonBasicPitchTranscriptionEngine(
            pythonLocator: new MockPythonLocator(true),
            processRunner: runner,
            workspaceService: _workspace,
            explicitWorkerScriptPath: Path.Combine(_tempRoot, "mock_worker.py")
        );

        File.WriteAllText(Path.Combine(_tempRoot, "mock_worker.py"), "# mock");

        // 1. Default / 88-key profile: pitch 21 and 108 must both be playable!
        var req88 = new TranscriptionRequest(jobId88, inputAudio);
        var result88 = await engine.TranscribeAsync(req88);

        Assert.True(result88.Success);
        Assert.Equal(2, result88.PlayableNoteCount);
        Assert.Equal(0, result88.OutOfRangeNoteCount);

        // 2. Explicit 61-key profile: pitch 21 and 108 are out-of-range
        var req61 = new TranscriptionRequest(jobId61, inputAudio, TargetPianoProfile: PianoProfileLoader.Load61KeyProfile());
        var result61 = await engine.TranscribeAsync(req61);

        Assert.True(result61.Success);
        Assert.Equal(0, result61.PlayableNoteCount);
        Assert.Equal(2, result61.OutOfRangeNoteCount);
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
