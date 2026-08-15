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
        private readonly TaskCompletionSource<int> _completionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsRunning { get; private set; } = true;
        public int? ProcessId => 9999;
        public Task<int> Completion => _completionTcs.Task;
        public bool KillCalled { get; private set; }
        public string? LastReceivedLine { get; private set; }
        private readonly Func<string, (string Type, string ResponseJson)>? _responder;

        public MockPythonSession(
            Action<string>? onStdOut,
            Func<string, (string Type, string ResponseJson)>? responder = null,
            string? customHelloJson = null,
            bool exitImmediately = false,
            int exitCode = 0,
            bool suppressHello = false)
        {
            _onStdOut = onStdOut;
            _responder = responder;

            if (exitImmediately)
            {
                IsRunning = false;
                _completionTcs.TrySetResult(exitCode);
                return;
            }

            if (suppressHello)
            {
                return;
            }

            // Emit hello handshake
            string hello = customHelloJson ?? "{\"type\":\"hello\",\"protocol\":1,\"request_id\":\"startup\",\"worker_version\":\"1.0.0\",\"python_version\":\"3.11.2\",\"basic_pitch_version\":\"0.4.0\",\"engine_available\":true,\"status_message\":\"정상\"}";
            _onStdOut?.Invoke(hello);
        }

        public void TriggerExit(int exitCode = 1)
        {
            IsRunning = false;
            _completionTcs.TrySetResult(exitCode);
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
            _completionTcs.TrySetResult(-1);
        }

        public void Dispose() => Kill();
    }

    private class MockProcessRunner : IPythonProcessRunner
    {
        public MockPythonSession? CurrentSession { get; private set; }
        private readonly Func<string, (string Type, string ResponseJson)>? _responder;
        private readonly string? _customHelloJson;
        private readonly bool _exitImmediately;
        private readonly int _exitCode;
        private readonly Queue<Func<Action<string>?, MockPythonSession>>? _sessionFactoryQueue;
        private readonly bool _suppressHello;

        public MockProcessRunner(
            Func<string, (string Type, string ResponseJson)>? responder = null,
            string? customHelloJson = null,
            bool exitImmediately = false,
            int exitCode = 0,
            Queue<Func<Action<string>?, MockPythonSession>>? sessionFactoryQueue = null,
            bool suppressHello = false)
        {
            _responder = responder;
            _customHelloJson = customHelloJson;
            _exitImmediately = exitImmediately;
            _exitCode = exitCode;
            _sessionFactoryQueue = sessionFactoryQueue;
            _suppressHello = suppressHello;
        }

        public Task<ProcessExecutionResult> RunProcessAsync(string executablePath, IReadOnlyList<string> arguments, Action<string>? onStdOutLine = null, Action<string>? onStdErrLine = null, TimeSpan? timeout = null, CancellationToken ct = default)
        {
            return Task.FromResult(ProcessExecutionResult.Success("Python 3.11.2"));
        }

        public IPythonProcessSession StartSession(string executablePath, IReadOnlyList<string> arguments, Action<string>? onStdOutLine = null, Action<string>? onStdErrLine = null, string? workingDir = null)
        {
            if (_sessionFactoryQueue != null && _sessionFactoryQueue.Count > 0)
            {
                var factory = _sessionFactoryQueue.Dequeue();
                CurrentSession = factory(onStdOutLine);
                return CurrentSession;
            }

            CurrentSession = new MockPythonSession(onStdOutLine, _responder, _customHelloJson, _exitImmediately, _exitCode, _suppressHello);
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

    [Fact]
    public async Task Startup_WorkerExitsBeforeHello_FailsPromptly()
    {
        string workerScript = Path.Combine(_tempRoot, "mock_worker.py");
        File.WriteAllText(workerScript, "# mock");

        var runner = new MockProcessRunner(exitImmediately: true, exitCode: 137);

        using var engine = new PythonBasicPitchTranscriptionEngine(
            pythonLocator: new MockPythonLocator(true),
            processRunner: runner,
            workspaceService: _workspace,
            explicitWorkerScriptPath: workerScript
        );

        string inputAudio = Path.Combine(_tempRoot, "audio.wav");
        File.WriteAllBytes(inputAudio, new byte[] { 1, 2, 3 });
        var req = new TranscriptionRequest("job_startup_exit", inputAudio);

        var result = await engine.TranscribeAsync(req);
        Assert.False(result.Success);
        Assert.Contains("AI 워커가 초기화 중 종료되었습니다", result.ErrorMessage);
    }

    [Fact]
    public async Task Transcription_WorkerExitsDuringJob_FailsPromptly()
    {
        string jobId = "job_crash_mid";
        string inputAudio = Path.Combine(_tempRoot, "audio.wav");
        File.WriteAllBytes(inputAudio, new byte[] { 1, 2, 3 });

        MockProcessRunner? runner = null;
        runner = new MockProcessRunner(line =>
        {
            // Simulate crash during execution without sending result
            Task.Run(() => runner?.CurrentSession?.TriggerExit(139));
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
        var result = await engine.TranscribeAsync(req);

        Assert.False(result.Success);
        Assert.Equal("WORKER_CRASHED", result.ErrorCode);
        Assert.Contains("종료되었습니다", result.ErrorMessage);
    }

    [Fact]
    public async Task Transcription_WorkerExit_CleansWorkspace()
    {
        string jobId = "job_crash_clean";
        string inputAudio = Path.Combine(_tempRoot, "audio.wav");
        File.WriteAllBytes(inputAudio, new byte[] { 1, 2, 3 });

        MockProcessRunner? runner = null;
        runner = new MockProcessRunner(line =>
        {
            // Worker writes a partial file then crashes
            string jobDir = _workspace.GetJobDirectory(jobId);
            File.WriteAllText(Path.Combine(jobDir, "temp.tmp"), "partial");
            Task.Run(() => runner?.CurrentSession?.TriggerExit(1));
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
        var result = await engine.TranscribeAsync(req);

        Assert.False(result.Success);
        Assert.Equal("WORKER_CRASHED", result.ErrorCode);
        Assert.False(Directory.Exists(_workspace.GetSafeJobDirectoryPath(jobId, createDirectory: false)));
    }

    [Fact]
    public async Task Transcription_WorkerExit_PreservesNormalizedAudio()
    {
        string jobId = "job_crash_preserve_audio";
        string inputAudio = Path.Combine(_tempRoot, "normalized_audio.wav");
        File.WriteAllBytes(inputAudio, new byte[] { 1, 2, 3, 4, 5 });

        MockProcessRunner? runner = null;
        runner = new MockProcessRunner(line =>
        {
            Task.Run(() => runner?.CurrentSession?.TriggerExit(1));
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
        var result = await engine.TranscribeAsync(req);

        Assert.False(result.Success);
        Assert.True(File.Exists(inputAudio), "Normalized audio file must remain preserved after crash");
    }

    [Fact]
    public async Task Transcription_WorkerExitThenNextJob_RestartsAndSucceeds()
    {
        string jobId1 = "job_crash_1";
        string jobId2 = "job_success_2";
        string inputAudio = Path.Combine(_tempRoot, "audio.wav");
        File.WriteAllBytes(inputAudio, new byte[] { 1, 2, 3 });

        var sessionQueue = new Queue<Func<Action<string>?, MockPythonSession>>();

        // Session 1: Crashes on request
        MockPythonSession? session1 = null;
        sessionQueue.Enqueue(onStdOut =>
        {
            session1 = new MockPythonSession(onStdOut, responder: line =>
            {
                Task.Run(() => session1?.TriggerExit(1));
                return ("none", "");
            });
            return session1;
        });

        // Session 2: Starts normally and responds with valid result
        sessionQueue.Enqueue(onStdOut =>
        {
            return new MockPythonSession(onStdOut, responder: line =>
            {
                using var doc = JsonDocument.Parse(line);
                string reqId = doc.RootElement.GetProperty("request_id").GetString()!;
                string jId = doc.RootElement.GetProperty("job_id").GetString()!;
                string expectedMidi = _workspace.GetFinalMidiPath(jId);

                // Write valid MIDI
                var midiFile = new Melanchall.DryWetMidi.Core.MidiFile(new Melanchall.DryWetMidi.Core.TrackChunk(
                    new Melanchall.DryWetMidi.Core.NoteOnEvent((Melanchall.DryWetMidi.Common.SevenBitNumber)60, (Melanchall.DryWetMidi.Common.SevenBitNumber)64) { DeltaTime = 0 },
                    new Melanchall.DryWetMidi.Core.NoteOffEvent((Melanchall.DryWetMidi.Common.SevenBitNumber)60, (Melanchall.DryWetMidi.Common.SevenBitNumber)0) { DeltaTime = 480 }
                ));
                midiFile.Write(expectedMidi, true);

                string resp = $"{{\"type\":\"result\",\"protocol\":1,\"request_id\":\"{reqId}\",\"job_id\":\"{jId}\",\"midi_path\":\"{expectedMidi.Replace("\\", "\\\\")}\",\"note_count\":1,\"duration_seconds\":1.0,\"min_pitch\":60,\"max_pitch\":60,\"runtime_seconds\":0.2,\"engine_version\":\"0.4.0\"}}";
                return ("result", resp);
            });
        });

        var runner = new MockProcessRunner(sessionFactoryQueue: sessionQueue);

        using var engine = new PythonBasicPitchTranscriptionEngine(
            pythonLocator: new MockPythonLocator(true),
            processRunner: runner,
            workspaceService: _workspace,
            explicitWorkerScriptPath: Path.Combine(_tempRoot, "mock_worker.py")
        );

        File.WriteAllText(Path.Combine(_tempRoot, "mock_worker.py"), "# mock");

        // First job: fails due to crash
        var req1 = new TranscriptionRequest(jobId1, inputAudio);
        var result1 = await engine.TranscribeAsync(req1);
        Assert.False(result1.Success);
        Assert.Equal("WORKER_CRASHED", result1.ErrorCode);

        // Second job: restarts new worker session and succeeds
        var req2 = new TranscriptionRequest(jobId2, inputAudio);
        var result2 = await engine.TranscribeAsync(req2);
        Assert.True(result2.Success);
        Assert.Equal(1, result2.NoteCount);
    }

    [Fact]
    public async Task Handshake_WrongProtocolVersion_FailsImmediately()
    {
        string workerScript = Path.Combine(_tempRoot, "mock_worker.py");
        File.WriteAllText(workerScript, "# mock");

        string helloProto2 = "{\"type\":\"hello\",\"protocol\":2,\"request_id\":\"startup\",\"worker_version\":\"1.0.0\",\"python_version\":\"3.11.2\",\"basic_pitch_version\":\"0.4.0\",\"engine_available\":true,\"status_message\":\"정상\"}";
        var runner = new MockProcessRunner(customHelloJson: helloProto2);

        using var engine = new PythonBasicPitchTranscriptionEngine(
            pythonLocator: new MockPythonLocator(true),
            processRunner: runner,
            explicitWorkerScriptPath: workerScript
        );

        var status = await engine.CheckAvailabilityAsync();
        Assert.False(status.IsAvailable);
        Assert.Contains("프로토콜 버전 불일치", status.StatusMessage);
    }

    [Fact]
    public async Task Handshake_MissingProtocol_FailsImmediately()
    {
        string workerScript = Path.Combine(_tempRoot, "mock_worker.py");
        File.WriteAllText(workerScript, "# mock");

        string helloNoProto = "{\"type\":\"hello\",\"request_id\":\"startup\",\"worker_version\":\"1.0.0\",\"python_version\":\"3.11.2\",\"basic_pitch_version\":\"0.4.0\",\"engine_available\":true,\"status_message\":\"정상\"}";
        var runner = new MockProcessRunner(customHelloJson: helloNoProto);

        using var engine = new PythonBasicPitchTranscriptionEngine(
            pythonLocator: new MockPythonLocator(true),
            processRunner: runner,
            explicitWorkerScriptPath: workerScript
        );

        var status = await engine.CheckAvailabilityAsync();
        Assert.False(status.IsAvailable);
        Assert.Contains("프로토콜", status.StatusMessage);
    }

    [Fact]
    public async Task Handshake_WrongRequestId_FailsImmediately()
    {
        string workerScript = Path.Combine(_tempRoot, "mock_worker.py");
        File.WriteAllText(workerScript, "# mock");

        string helloWrongReq = "{\"type\":\"hello\",\"protocol\":1,\"request_id\":\"other\",\"worker_version\":\"1.0.0\",\"python_version\":\"3.11.2\",\"basic_pitch_version\":\"0.4.0\",\"engine_available\":true,\"status_message\":\"정상\"}";
        var runner = new MockProcessRunner(customHelloJson: helloWrongReq);

        using var engine = new PythonBasicPitchTranscriptionEngine(
            pythonLocator: new MockPythonLocator(true),
            processRunner: runner,
            explicitWorkerScriptPath: workerScript
        );

        var status = await engine.CheckAvailabilityAsync();
        Assert.False(status.IsAvailable);
        Assert.Contains("request_id", status.StatusMessage);
    }

    [Fact]
    public async Task Result_WrongProtocolVersion_FailsCurrentJob()
    {
        string jobId = "job_res_proto2";
        string inputAudio = Path.Combine(_tempRoot, "audio.wav");
        File.WriteAllBytes(inputAudio, new byte[] { 1, 2, 3 });

        var runner = new MockProcessRunner(line =>
        {
            using var doc = JsonDocument.Parse(line);
            string reqId = doc.RootElement.GetProperty("request_id").GetString()!;
            string jId = doc.RootElement.GetProperty("job_id").GetString()!;

            string resp = $"{{\"type\":\"result\",\"protocol\":2,\"request_id\":\"{reqId}\",\"job_id\":\"{jId}\",\"midi_path\":\"dummy.mid\"}}";
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
        Assert.Contains("프로토콜 버전 불일치", result.ErrorMessage);
    }

    [Fact]
    public async Task Error_WrongProtocolVersion_FailsCurrentJob()
    {
        string jobId = "job_err_proto2";
        string inputAudio = Path.Combine(_tempRoot, "audio.wav");
        File.WriteAllBytes(inputAudio, new byte[] { 1, 2, 3 });

        var runner = new MockProcessRunner(line =>
        {
            using var doc = JsonDocument.Parse(line);
            string reqId = doc.RootElement.GetProperty("request_id").GetString()!;
            string jId = doc.RootElement.GetProperty("job_id").GetString()!;

            string resp = $"{{\"type\":\"error\",\"protocol\":99,\"request_id\":\"{reqId}\",\"job_id\":\"{jId}\",\"error_code\":\"FAIL\"}}";
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
        Assert.Contains("프로토콜 버전 불일치", result.ErrorMessage);
    }

    [Fact]
    public async Task Status_WrongProtocolVersion_FailsCurrentJob()
    {
        string jobId = "job_stat_proto2";
        string inputAudio = Path.Combine(_tempRoot, "audio.wav");
        File.WriteAllBytes(inputAudio, new byte[] { 1, 2, 3 });

        var runner = new MockProcessRunner(line =>
        {
            using var doc = JsonDocument.Parse(line);
            string reqId = doc.RootElement.GetProperty("request_id").GetString()!;
            string jId = doc.RootElement.GetProperty("job_id").GetString()!;

            // Send corrupted status first
            string badStatus = $"{{\"type\":\"status\",\"protocol\":0,\"request_id\":\"{reqId}\",\"job_id\":\"{jId}\",\"phase\":\"transcribing\"}}";
            return ("status", badStatus);
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
        Assert.Contains("프로토콜 버전 불일치", result.ErrorMessage);
    }

    [Fact]
    public async Task MalformedEnvelope_DoesNotHang()
    {
        string jobId = "job_malformed";
        string inputAudio = Path.Combine(_tempRoot, "audio.wav");
        File.WriteAllBytes(inputAudio, new byte[] { 1, 2, 3 });

        var runner = new MockProcessRunner(line =>
        {
            // Send invalid json
            return ("raw", "{not-valid-json");
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
        Assert.Contains("파싱 오류", result.ErrorMessage);
    }

    [Fact]
    public async Task Startup_AliveWorkerWithoutHello_TimesOut()
    {
        string workerScript = Path.Combine(_tempRoot, "mock_worker.py");
        File.WriteAllText(workerScript, "# mock");

        var runner = new MockProcessRunner(suppressHello: true);

        using var engine = new PythonBasicPitchTranscriptionEngine(
            pythonLocator: new MockPythonLocator(true),
            processRunner: runner,
            workspaceService: _workspace,
            explicitWorkerScriptPath: workerScript,
            startupHandshakeTimeout: TimeSpan.FromMilliseconds(100)
        );

        string inputAudio = Path.Combine(_tempRoot, "audio.wav");
        File.WriteAllBytes(inputAudio, new byte[] { 1, 2, 3 });
        var req = new TranscriptionRequest("job_startup_timeout", inputAudio);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await engine.TranscribeAsync(req);
        sw.Stop();

        Assert.False(result.Success);
        Assert.Contains("초과", result.ErrorMessage);
        Assert.True(sw.ElapsedMilliseconds < 5000, "Should timeout fast without waiting 30 seconds");
    }

    [Fact]
    public async Task Startup_UserCancellationBeforeHello_CancelsImmediately()
    {
        string workerScript = Path.Combine(_tempRoot, "mock_worker.py");
        File.WriteAllText(workerScript, "# mock");

        var runner = new MockProcessRunner(suppressHello: true);

        using var engine = new PythonBasicPitchTranscriptionEngine(
            pythonLocator: new MockPythonLocator(true),
            processRunner: runner,
            workspaceService: _workspace,
            explicitWorkerScriptPath: workerScript,
            startupHandshakeTimeout: TimeSpan.FromSeconds(30)
        );

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(60));

        string inputAudio = Path.Combine(_tempRoot, "audio.wav");
        File.WriteAllBytes(inputAudio, new byte[] { 1, 2, 3 });
        var req = new TranscriptionRequest("job_user_cancel", inputAudio);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await engine.TranscribeAsync(req, ct: cts.Token);
        });
        sw.Stop();

        Assert.True(runner.CurrentSession?.KillCalled);
        Assert.False(runner.CurrentSession?.IsRunning);
        Assert.True(sw.ElapsedMilliseconds < 5000, "Should cancel immediately without waiting 30 seconds");
    }

    [Fact]
    public async Task Startup_WorkerExitBeforeHello_FailsBeforeTimeout()
    {
        string workerScript = Path.Combine(_tempRoot, "mock_worker.py");
        File.WriteAllText(workerScript, "# mock");

        var runner = new MockProcessRunner(exitImmediately: true, exitCode: 137);

        using var engine = new PythonBasicPitchTranscriptionEngine(
            pythonLocator: new MockPythonLocator(true),
            processRunner: runner,
            workspaceService: _workspace,
            explicitWorkerScriptPath: workerScript,
            startupHandshakeTimeout: TimeSpan.FromSeconds(30)
        );

        string inputAudio = Path.Combine(_tempRoot, "audio.wav");
        File.WriteAllBytes(inputAudio, new byte[] { 1, 2, 3 });
        var req = new TranscriptionRequest("job_exit_before_timeout", inputAudio);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await engine.TranscribeAsync(req);
        sw.Stop();

        Assert.False(result.Success);
        Assert.Contains("AI 워커가 초기화 중 종료되었습니다", result.ErrorMessage);
        Assert.True(sw.ElapsedMilliseconds < 5000, "Worker exit should immediately terminate without waiting for timeout");
    }

    [Fact]
    public async Task Startup_ValidHelloBeforeTimeout_Succeeds()
    {
        string workerScript = Path.Combine(_tempRoot, "mock_worker.py");
        File.WriteAllText(workerScript, "# mock");

        var runner = new MockProcessRunner();

        using var engine = new PythonBasicPitchTranscriptionEngine(
            pythonLocator: new MockPythonLocator(true),
            processRunner: runner,
            explicitWorkerScriptPath: workerScript,
            startupHandshakeTimeout: TimeSpan.FromMilliseconds(500)
        );

        var status = await engine.CheckAvailabilityAsync();
        Assert.True(status.IsAvailable);
        Assert.Equal("0.4.0", status.BasicPitchVersion);
    }

    [Fact]
    public async Task Startup_Timeout_KillsOwnedWorker()
    {
        string workerScript = Path.Combine(_tempRoot, "mock_worker.py");
        File.WriteAllText(workerScript, "# mock");

        var runner = new MockProcessRunner(suppressHello: true);

        using var engine = new PythonBasicPitchTranscriptionEngine(
            pythonLocator: new MockPythonLocator(true),
            processRunner: runner,
            explicitWorkerScriptPath: workerScript,
            startupHandshakeTimeout: TimeSpan.FromMilliseconds(80)
        );

        var status = await engine.CheckAvailabilityAsync();
        Assert.False(status.IsAvailable);
        Assert.True(runner.CurrentSession?.KillCalled);
        Assert.False(runner.CurrentSession?.IsRunning);
    }

    [Fact]
    public async Task Startup_Timeout_DoesNotLeaveSessionRunning()
    {
        string workerScript = Path.Combine(_tempRoot, "mock_worker.py");
        File.WriteAllText(workerScript, "# mock");

        var runner = new MockProcessRunner(suppressHello: true);

        using var engine = new PythonBasicPitchTranscriptionEngine(
            pythonLocator: new MockPythonLocator(true),
            processRunner: runner,
            workspaceService: _workspace,
            explicitWorkerScriptPath: workerScript,
            startupHandshakeTimeout: TimeSpan.FromMilliseconds(80)
        );

        string inputAudio = Path.Combine(_tempRoot, "audio.wav");
        File.WriteAllBytes(inputAudio, new byte[] { 1, 2, 3 });
        var req = new TranscriptionRequest("job_no_orphan", inputAudio);

        var result = await engine.TranscribeAsync(req);
        Assert.False(result.Success);
        Assert.True(runner.CurrentSession?.KillCalled);
        Assert.False(runner.CurrentSession?.IsRunning);
    }

    [Fact]
    public async Task CheckAvailability_HandshakeTimeout_ReturnsUnavailable()
    {
        string workerScript = Path.Combine(_tempRoot, "mock_worker.py");
        File.WriteAllText(workerScript, "# mock");

        var runner = new MockProcessRunner(suppressHello: true);

        using var engine = new PythonBasicPitchTranscriptionEngine(
            pythonLocator: new MockPythonLocator(true),
            processRunner: runner,
            explicitWorkerScriptPath: workerScript,
            startupHandshakeTimeout: TimeSpan.FromMilliseconds(80)
        );

        var status = await engine.CheckAvailabilityAsync();
        Assert.False(status.IsAvailable);
        Assert.Contains("초과", status.StatusMessage);
    }
}
