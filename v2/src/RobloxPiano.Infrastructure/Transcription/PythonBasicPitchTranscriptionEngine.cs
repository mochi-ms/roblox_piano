using System.IO;
using System.Text;
using System.Text.Json;
using RobloxPiano.Core.Importing;
using RobloxPiano.Core.Transcription;

namespace RobloxPiano.Infrastructure.Transcription;

public class PythonBasicPitchTranscriptionEngine : ITranscriptionEngine
{
    private readonly IPythonLocator _pythonLocator;
    private readonly IPythonProcessRunner _processRunner;
    private readonly IImportPipeline _importPipeline;
    private readonly TranscriptionWorkspaceService _workspaceService;
    private readonly string? _explicitPythonPath;
    private readonly string? _explicitWorkerScriptPath;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private IPythonProcessSession? _session;
    private TaskCompletionSource<JsonElement>? _handshakeTcs;
    private TaskCompletionSource<JsonElement>? _currentRequestTcs;
    private string? _activeRequestId;
    private string? _activeJobId;
    private IProgress<TranscriptionProgress>? _activeProgress;
    private readonly StringBuilder _rollingStderr = new();
    private bool _disposed;

    private string? _detectedPythonVersion;
    private string? _detectedBasicPitchVersion;
    private bool _engineAvailable;
    private string? _detectedStatusMessage;

    public PythonBasicPitchTranscriptionEngine(
        IPythonLocator? pythonLocator = null,
        IPythonProcessRunner? processRunner = null,
        IImportPipeline? importPipeline = null,
        TranscriptionWorkspaceService? workspaceService = null,
        string? explicitPythonPath = null,
        string? explicitWorkerScriptPath = null)
    {
        _processRunner = processRunner ?? new PythonProcessRunner();
        _pythonLocator = pythonLocator ?? new PythonLocator(_processRunner);
        _importPipeline = importPipeline ?? new ImportPipeline();
        _workspaceService = workspaceService ?? new TranscriptionWorkspaceService();
        _explicitPythonPath = explicitPythonPath;
        _explicitWorkerScriptPath = explicitWorkerScriptPath;
    }

    public async Task<TranscriptionEngineStatus> CheckAvailabilityAsync(CancellationToken ct = default)
    {
        var (pyPath, pyVer, is311) = await _pythonLocator.LocatePythonAsync(_explicitPythonPath, ct);
        if (pyPath == null || !is311)
        {
            return TranscriptionEngineStatus.Unavailable(TranscriptionError.PythonNotFound);
        }

        string workerScript = ResolveWorkerScriptPath();
        if (!File.Exists(workerScript))
        {
            return TranscriptionEngineStatus.Unavailable("worker.py 스크립트 파일을 찾을 수 없습니다.");
        }

        try
        {
            await _lock.WaitAsync(ct);
            try
            {
                await EnsureWorkerRunningAsync(null, ct);

                if (!string.Equals(_detectedBasicPitchVersion, "0.4.0", StringComparison.OrdinalIgnoreCase))
                {
                    return TranscriptionEngineStatus.Unavailable(
                        string.IsNullOrWhiteSpace(_detectedStatusMessage)
                            ? $"Basic Pitch 0.4.0이 필요하지만 현재 '{_detectedBasicPitchVersion ?? "없음"}'입니다."
                            : _detectedStatusMessage
                    );
                }

                if (!_engineAvailable)
                {
                    return TranscriptionEngineStatus.Unavailable(
                        string.IsNullOrWhiteSpace(_detectedStatusMessage)
                            ? "Basic Pitch AI 엔진을 사용할 수 없습니다."
                            : _detectedStatusMessage
                    );
                }

                return TranscriptionEngineStatus.Available(
                    pyPath,
                    pyVer ?? "3.11",
                    _detectedBasicPitchVersion ?? "0.4.0"
                );
            }
            finally
            {
                _lock.Release();
            }
        }
        catch (Exception ex)
        {
            return TranscriptionEngineStatus.Unavailable($"Basic Pitch 가용성 확인 실패: {ex.Message}");
        }
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        TranscriptionRequest request,
        IProgress<TranscriptionProgress>? progress = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.NormalizedAudioPath) || !File.Exists(request.NormalizedAudioPath))
        {
            return TranscriptionResult.Failed(request.JobId, request.NormalizedAudioPath, TranscriptionError.InvalidInputAudio, "INVALID_INPUT_AUDIO");
        }

        if (!TranscriptionWorkspaceService.IsValidJobId(request.JobId))
        {
            return TranscriptionResult.Failed(request.JobId, request.NormalizedAudioPath, "유효하지 않거나 안전하지 않은 작업 ID입니다.", "INVALID_JOB_ID");
        }

        try
        {
            request.EffectiveOptions.Validate();
        }
        catch (Exception ex)
        {
            return TranscriptionResult.Failed(request.JobId, request.NormalizedAudioPath, $"옵션 유효성 검사 실패: {ex.Message}", "INVALID_OPTIONS");
        }

        await _lock.WaitAsync(ct);

        var workspace = !string.IsNullOrWhiteSpace(request.OutputWorkspaceRoot)
            ? new TranscriptionWorkspaceService(request.OutputWorkspaceRoot)
            : _workspaceService;

        string outputDir = workspace.GetJobDirectory(request.JobId);
        string expectedMidiPath = workspace.GetFinalMidiPath(request.JobId);

        bool committedSuccess = false;

        try
        {
            ct.ThrowIfCancellationRequested();

            progress?.Report(TranscriptionProgress.Starting());
            await EnsureWorkerRunningAsync(progress, ct);

            string reqId = Guid.NewGuid().ToString("N");
            _activeRequestId = reqId;
            _activeJobId = request.JobId;
            _activeProgress = progress;

            _currentRequestTcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

            var reqObj = new
            {
                type = "transcribe",
                protocol = 1,
                request_id = reqId,
                job_id = request.JobId,
                audio_path = Path.GetFullPath(request.NormalizedAudioPath),
                output_dir = Path.GetFullPath(outputDir),
                options = new
                {
                    onset_threshold = request.EffectiveOptions.OnsetThreshold,
                    frame_threshold = request.EffectiveOptions.FrameThreshold,
                    minimum_note_length_ms = request.EffectiveOptions.MinimumNoteLengthMs
                }
            };

            string jsonLine = JsonSerializer.Serialize(reqObj);
            var session = _session!;
            var requestTask = _currentRequestTcs.Task;
            var exitTask = session.Completion;

            using var reg = ct.Register(() =>
            {
                KillSession();
                _currentRequestTcs.TrySetCanceled(ct);
            });

            await session.SendLineAsync(jsonLine, ct);

            var firstCompleted = await Task.WhenAny(requestTask, exitTask);
            if (ct.IsCancellationRequested)
            {
                ct.ThrowIfCancellationRequested();
            }

            if (firstCompleted == exitTask)
            {
                int exitCode = await exitTask;
                KillSession();
                workspace.CleanJob(request.JobId);
                string stderr = TruncateDiagnostic(_rollingStderr.ToString(), 1024);
                return TranscriptionResult.Failed(
                    request.JobId,
                    request.NormalizedAudioPath,
                    $"AI 워커 프로세스가 작업 수행 중 예기치 않게 종료되었습니다. (종료 코드: {exitCode}) {stderr}".Trim(),
                    "WORKER_CRASHED"
                );
            }

            JsonElement responseElement;
            try
            {
                responseElement = await requestTask;
            }
            catch (OperationCanceledException)
            {
                KillSession();
                workspace.CleanJob(request.JobId);
                throw;
            }
            catch (Exception ex)
            {
                KillSession();
                workspace.CleanJob(request.JobId);
                return TranscriptionResult.Failed(request.JobId, request.NormalizedAudioPath, $"{TranscriptionError.InferenceFailed}: {TruncateDiagnostic(ex.Message)}", "INFERENCE_ERROR");
            }

            ct.ThrowIfCancellationRequested();

            // 1. Verify response type
            string resType = responseElement.GetProperty("type").GetString() ?? "";
            if (string.Equals(resType, "error", StringComparison.OrdinalIgnoreCase))
            {
                string errCode = responseElement.TryGetProperty("error_code", out var ecProp) ? ecProp.GetString() ?? "INFERENCE_FAILED" : "INFERENCE_FAILED";
                string errMsg = responseElement.TryGetProperty("error_message", out var emProp) ? emProp.GetString() ?? TranscriptionError.InferenceFailed : TranscriptionError.InferenceFailed;
                workspace.CleanJob(request.JobId);
                return TranscriptionResult.Failed(request.JobId, request.NormalizedAudioPath, errMsg, errCode);
            }

            // 2. Verify generated MIDI file
            string returnedMidiPath = responseElement.TryGetProperty("midi_path", out var mpProp) ? mpProp.GetString() ?? "" : "";
            double runtimeSec = responseElement.TryGetProperty("runtime_seconds", out var rtProp) && rtProp.TryGetDouble(out double rtVal) ? rtVal : 0.0;
            string engineVer = responseElement.TryGetProperty("engine_version", out var evProp) ? evProp.GetString() ?? "0.4.0" : "0.4.0";

            if (string.IsNullOrWhiteSpace(returnedMidiPath) || !File.Exists(returnedMidiPath) || new FileInfo(returnedMidiPath).Length == 0)
            {
                workspace.CleanJob(request.JobId);
                return TranscriptionResult.Failed(request.JobId, request.NormalizedAudioPath, TranscriptionError.MidiWriteFailed, "MIDI_WRITE_FAILED", runtimeSec, engineVersion: engineVer);
            }

            // Strict path trust and workspace containment validation
            string fullExpected = Path.GetFullPath(expectedMidiPath);
            string fullReturned = Path.GetFullPath(returnedMidiPath);
            string jobDir = Path.GetFullPath(outputDir);

            if (!string.Equals(fullExpected, fullReturned, StringComparison.OrdinalIgnoreCase) ||
                !fullReturned.StartsWith(jobDir, StringComparison.OrdinalIgnoreCase))
            {
                workspace.CleanJob(request.JobId);
                return TranscriptionResult.Failed(request.JobId, request.NormalizedAudioPath, "워커가 반환한 MIDI 경로가 작업 디렉터리 내 예상 경로와 일치하지 않습니다.", "UNTRUSTED_MIDI_PATH", runtimeSec, engineVersion: engineVer);
            }

            progress?.Report(TranscriptionProgress.Validating());
            ct.ThrowIfCancellationRequested();

            // 3. Validate generated MIDI through existing ImportPipeline with profile-awareness
            var importReq = new ImportRequest(
                returnedMidiPath,
                request.SourceTitle ?? Path.GetFileNameWithoutExtension(request.NormalizedAudioPath),
                targetFolderId: null,
                addToLibrary: false,
                targetPianoProfile: request.TargetPianoProfile
            );

            var importResult = await _importPipeline.ImportFileAsync(importReq, ct: ct);
            if (!importResult.Success || importResult.Timeline == null || importResult.Timeline.Notes.Count == 0)
            {
                workspace.CleanJob(request.JobId);
                string err = !string.IsNullOrWhiteSpace(importResult.ErrorMessage) ? importResult.ErrorMessage : TranscriptionError.NoNotes;
                return TranscriptionResult.Failed(request.JobId, request.NormalizedAudioPath, err, "VALIDATION_FAILED", runtimeSec, engineVersion: engineVer);
            }

            ct.ThrowIfCancellationRequested();

            var timeline = importResult.Timeline;
            int playable = importResult.PlayableNoteCount;
            int outOfRange = importResult.OutOfRangeNoteCount;
            int? minPitch = importResult.MinPitch;
            int? maxPitch = importResult.MaxPitch;

            committedSuccess = true;
            progress?.Report(TranscriptionProgress.Completed());

            return TranscriptionResult.Successful(
                request.JobId,
                request.NormalizedAudioPath,
                returnedMidiPath,
                timeline,
                playable,
                outOfRange,
                minPitch,
                maxPitch,
                runtimeSec,
                engineName: "Basic Pitch",
                engineVersion: engineVer
            );
        }
        catch (OperationCanceledException)
        {
            KillSession();
            workspace.CleanJob(request.JobId);
            throw;
        }
        catch (Exception ex)
        {
            KillSession();
            workspace.CleanJob(request.JobId);
            return TranscriptionResult.Failed(request.JobId, request.NormalizedAudioPath, $"{TranscriptionError.InferenceFailed}: {TruncateDiagnostic(ex.Message)}", "UNEXPECTED_ERROR");
        }
        finally
        {
            _activeRequestId = null;
            _activeJobId = null;
            _activeProgress = null;
            _currentRequestTcs = null;

            if (!committedSuccess)
            {
                workspace.CleanJob(request.JobId);
            }

            _lock.Release();
        }
    }

    private async Task EnsureWorkerRunningAsync(IProgress<TranscriptionProgress>? progress, CancellationToken ct)
    {
        if (_session != null && _session.IsRunning)
        {
            return;
        }

        KillSession();

        var (pyPath, pyVer, is311) = await _pythonLocator.LocatePythonAsync(_explicitPythonPath, ct);
        if (pyPath == null || !is311)
        {
            throw new InvalidOperationException(TranscriptionError.PythonNotFound);
        }

        _detectedPythonVersion = pyVer;

        string workerScript = ResolveWorkerScriptPath();
        if (!File.Exists(workerScript))
        {
            throw new FileNotFoundException($"worker.py 스크립트를 찾을 수 없습니다: {workerScript}", workerScript);
        }

        _handshakeTcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        string? workingDir = Path.GetDirectoryName(workerScript);

        _session = _processRunner.StartSession(
            pyPath,
            new[] { "-u", workerScript },
            onStdOutLine: HandleStdOutLine,
            onStdErrLine: HandleStdErrLine,
            workingDir: workingDir
        );

        using var ctsTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, ctsTimeout.Token);

        var handshakeTask = _handshakeTcs.Task;
        var exitTask = _session.Completion;

        var firstCompleted = await Task.WhenAny(handshakeTask, exitTask);
        if (linkedCts.Token.IsCancellationRequested)
        {
            linkedCts.Token.ThrowIfCancellationRequested();
        }

        if (firstCompleted == exitTask)
        {
            int exitCode = await exitTask;
            KillSession();
            string stderr = TruncateDiagnostic(_rollingStderr.ToString(), 1024);
            throw new InvalidOperationException($"AI 워커가 초기화 중 종료되었습니다. (종료 코드: {exitCode}) {stderr}".Trim());
        }

        try
        {
            var helloJson = await handshakeTask.WaitAsync(linkedCts.Token);
            if (helloJson.TryGetProperty("basic_pitch_version", out var bpProp))
            {
                _detectedBasicPitchVersion = bpProp.GetString();
            }
            if (helloJson.TryGetProperty("engine_available", out var eaProp))
            {
                _engineAvailable = eaProp.ValueKind == JsonValueKind.True;
            }
            if (helloJson.TryGetProperty("status_message", out var smProp))
            {
                _detectedStatusMessage = smProp.GetString();
            }

            if (!_engineAvailable || !string.Equals(_detectedBasicPitchVersion, "0.4.0", StringComparison.OrdinalIgnoreCase))
            {
                string err = !string.IsNullOrWhiteSpace(_detectedStatusMessage)
                    ? _detectedStatusMessage
                    : $"Basic Pitch 0.4.0이 필요합니다 (현재: {_detectedBasicPitchVersion ?? "없음"}).";
                throw new InvalidOperationException(err);
            }
        }
        catch (OperationCanceledException)
        {
            KillSession();
            if (ctsTimeout.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                throw new TimeoutException("AI 워커 프로세스 초기화 핸드셰이크(Hello) 시간이 초과되었습니다.");
            }
            throw;
        }
        catch (Exception ex)
        {
            KillSession();
            throw new InvalidOperationException($"AI 워커 프로세스 시작 실패: {ex.Message}", ex);
        }
    }

    private void HandleStdOutLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        if (line.Length > 1_048_576) // 1MB protection
        {
            var ex = new InvalidOperationException("워커 출력 크기가 허용치(1MB)를 초과했습니다.");
            _handshakeTcs?.TrySetException(ex);
            _currentRequestTcs?.TrySetException(ex);
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement.Clone();

            if (!root.TryGetProperty("type", out var typeProp) || string.IsNullOrWhiteSpace(typeProp.GetString()))
            {
                var ex = new InvalidOperationException("프로토콜 오류: 메시지 타입('type') 필드가 누락되었거나 유효하지 않습니다.");
                _handshakeTcs?.TrySetException(ex);
                _currentRequestTcs?.TrySetException(ex);
                return;
            }

            string type = typeProp.GetString()!;

            // Strict protocol version 1 check
            if (!root.TryGetProperty("protocol", out var protoProp) || protoProp.ValueKind != JsonValueKind.Number || protoProp.GetInt32() != 1)
            {
                int protoVal = (protoProp.ValueKind == JsonValueKind.Number) ? protoProp.GetInt32() : -1;
                var ex = new InvalidOperationException($"프로토콜 버전 불일치: 지원되지 않는 프로토콜 버전 {protoVal} (기대치: 1)");
                _handshakeTcs?.TrySetException(ex);
                _currentRequestTcs?.TrySetException(ex);
                return;
            }

            if (!root.TryGetProperty("request_id", out var reqIdProp) || string.IsNullOrWhiteSpace(reqIdProp.GetString()))
            {
                var ex = new InvalidOperationException("프로토콜 오류: 'request_id' 필드가 누락되었습니다.");
                _handshakeTcs?.TrySetException(ex);
                _currentRequestTcs?.TrySetException(ex);
                return;
            }

            string reqId = reqIdProp.GetString()!;

            if (string.Equals(type, "hello", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(reqId, "startup", StringComparison.Ordinal))
                {
                    _handshakeTcs?.TrySetException(new InvalidOperationException("프로토콜 오류: hello 메시지의 request_id가 'startup'이 아닙니다."));
                    return;
                }

                if (!root.TryGetProperty("python_version", out _) || !root.TryGetProperty("basic_pitch_version", out _))
                {
                    _handshakeTcs?.TrySetException(new InvalidOperationException("프로토콜 오류: hello 메시지에 필수 버전 정보가 누락되었습니다."));
                    return;
                }

                _handshakeTcs?.TrySetResult(root);
                return;
            }

            // For status, result, and error messages
            string jId = root.TryGetProperty("job_id", out var jProp) ? jProp.GetString() ?? "" : "";

            if (string.Equals(type, "status", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(reqId, _activeRequestId, StringComparison.Ordinal) &&
                    string.Equals(jId, _activeJobId, StringComparison.Ordinal))
                {
                    string phase = root.TryGetProperty("phase", out var pProp) ? pProp.GetString() ?? "" : "";
                    string msg = root.TryGetProperty("message", out var mProp) ? mProp.GetString() ?? "" : "";

                    var progressPhase = phase switch
                    {
                        "model_loading" => TranscriptionPhase.ModelLoading,
                        "model_ready" => TranscriptionPhase.ModelReady,
                        "transcribing" => TranscriptionPhase.Transcribing,
                        "writing_midi" => TranscriptionPhase.WritingMidi,
                        _ => TranscriptionPhase.Transcribing
                    };

                    _activeProgress?.Report(new TranscriptionProgress(progressPhase, msg, true));
                }
                return;
            }

            if (string.Equals(type, "result", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "error", StringComparison.OrdinalIgnoreCase))
            {
                // Match request and job ID
                if (string.Equals(reqId, _activeRequestId, StringComparison.Ordinal) &&
                    (string.IsNullOrEmpty(_activeJobId) || string.Equals(jId, _activeJobId, StringComparison.Ordinal)))
                {
                    _currentRequestTcs?.TrySetResult(root);
                }
                return;
            }

            // Unknown message type
            var unknownTypeEx = new InvalidOperationException($"알 수 없는 프로토콜 메시지 타입: '{type}'");
            _currentRequestTcs?.TrySetException(unknownTypeEx);
        }
        catch (Exception ex)
        {
            _handshakeTcs?.TrySetException(new InvalidOperationException($"워커 프로토콜 파싱 오류: {ex.Message}"));
            _currentRequestTcs?.TrySetException(new InvalidOperationException($"워커 프로토콜 파싱 오류: {ex.Message}"));
        }
    }

    private void HandleStdErrLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        lock (_rollingStderr)
        {
            _rollingStderr.AppendLine(line);
            if (_rollingStderr.Length > 65536) // 64 KB rolling bound
            {
                _rollingStderr.Remove(0, _rollingStderr.Length - 32768);
            }
        }
    }

    private string ResolveWorkerScriptPath()
    {
        if (!string.IsNullOrWhiteSpace(_explicitWorkerScriptPath))
        {
            return Path.GetFullPath(_explicitWorkerScriptPath);
        }

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "python_worker", "worker.py"),
            Path.Combine(baseDir, "..", "..", "..", "..", "python_worker", "worker.py"),
            Path.Combine(baseDir, "..", "..", "..", "python_worker", "worker.py"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RobloxPianoPlayer", "python_worker", "worker.py")
        };

        foreach (var cand in candidates)
        {
            if (File.Exists(cand))
            {
                return Path.GetFullPath(cand);
            }
        }

        return Path.Combine(baseDir, "python_worker", "worker.py");
    }

    private void KillSession()
    {
        try
        {
            _session?.Kill();
            _session?.Dispose();
        }
        catch { }
        finally
        {
            _session = null;
        }
    }

    private static string TruncateDiagnostic(string? text, int maxLength = 4096)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        text = text.Trim();
        return text.Length <= maxLength ? text : text[..maxLength] + "... [진단 로그 축약됨]";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        KillSession();
        _lock.Dispose();
    }
}
