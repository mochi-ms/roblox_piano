using System.IO;
using RobloxPiano.Core.Audio;

namespace RobloxPiano.Infrastructure.Audio;

public class AudioIngestionService : IAudioIngestionService
{
    private readonly IFfmpegToolLocator _toolLocator;
    private readonly IFfmpegProcessRunner _processRunner;
    private readonly FfprobeMetadataReader _metadataReader;
    private readonly AudioWorkspaceService _workspaceService;

    public const long MaxFileSizeBytes = 500L * 1024 * 1024; // 500 MB limit

    public AudioIngestionService(
        IFfmpegToolLocator? toolLocator = null,
        IFfmpegProcessRunner? processRunner = null,
        FfprobeMetadataReader? metadataReader = null,
        AudioWorkspaceService? workspaceService = null)
    {
        _processRunner = processRunner ?? new FfmpegProcessRunner();
        _toolLocator = toolLocator ?? new FfmpegToolLocator(_processRunner);
        _metadataReader = metadataReader ?? new FfprobeMetadataReader(_processRunner);
        _workspaceService = workspaceService ?? new AudioWorkspaceService();
    }

    public async Task<AudioIngestResult> IngestAudioAsync(
        AudioIngestRequest request,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.FilePath))
        {
            return AudioIngestResult.Failed(request.FilePath ?? string.Empty, AudioError.FileNotFound, "FILE_NOT_FOUND", request.JobId);
        }

        // Validate Job ID safety before proceeding
        if (!AudioWorkspaceService.IsValidJobId(request.JobId))
        {
            return AudioIngestResult.Failed(request.FilePath, "유효하지 않거나 안전하지 않은 작업 ID입니다.", "INVALID_JOB_ID", request.JobId);
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(request.FilePath);
        }
        catch (Exception ex)
        {
            return AudioIngestResult.Failed(request.FilePath, $"{AudioError.FileNotFound}: {ex.Message}", "FILE_NOT_FOUND", request.JobId);
        }

        if (!File.Exists(fullPath))
        {
            return AudioIngestResult.Failed(fullPath, AudioError.FileNotFound, "FILE_NOT_FOUND", request.JobId);
        }

        // 1. Extension validation
        var sourceType = AudioSourceTypeExtensions.FromExtension(fullPath);
        if (sourceType == AudioSourceType.Unknown)
        {
            return AudioIngestResult.Failed(fullPath, AudioError.UnsupportedExtension, "UNSUPPORTED_EXTENSION", request.JobId);
        }

        // 2. File size validation
        var fileInfo = new FileInfo(fullPath);
        if (fileInfo.Length > MaxFileSizeBytes)
        {
            return AudioIngestResult.Failed(fullPath, AudioError.FileTooLarge, "FILE_TOO_LARGE", request.JobId);
        }

        ct.ThrowIfCancellationRequested();

        // 3. Locate FFmpeg & FFprobe
        var tools = await _toolLocator.LocateToolsAsync(ct: ct);
        if (!tools.IsFfmpegAvailable)
        {
            return AudioIngestResult.Failed(fullPath, AudioError.FfmpegNotFound, "FFMPEG_NOT_FOUND", request.JobId);
        }

        if (!tools.IsFfprobeAvailable)
        {
            return AudioIngestResult.Failed(fullPath, AudioError.FfprobeNotFound, "FFPROBE_NOT_FOUND", request.JobId);
        }

        ct.ThrowIfCancellationRequested();

        // 4. Probe source file metadata
        var probeResult = await _metadataReader.ProbeFileAsync(tools.FfprobePath!, fullPath, ct);
        if (!probeResult.IsValid || probeResult.Metadata == null)
        {
            return AudioIngestResult.Failed(
                fullPath,
                probeResult.ErrorMessage ?? AudioError.InvalidMedia,
                "PROBE_FAILED",
                request.JobId,
                probeResult.Metadata);
        }

        var metadata = probeResult.Metadata;

        ct.ThrowIfCancellationRequested();

        // 5. Prepare workspace with scoped lifecycle
        var workspace = !string.IsNullOrWhiteSpace(request.CustomWorkspaceDir)
            ? new AudioWorkspaceService(request.CustomWorkspaceDir)
            : _workspaceService;

        string tempOutputPath = workspace.GetTempNormalizedPath(request.JobId);
        bool committedSuccess = false;

        try
        {
            // Remove any existing temp file before conversion
            if (File.Exists(tempOutputPath))
            {
                try { File.Delete(tempOutputPath); } catch { }
            }

            ct.ThrowIfCancellationRequested();

            // 6. FFmpeg conversion to Canonical WAV (mono, 22050 Hz, PCM 16-bit)
            var ffmpegArgs = new[]
            {
                "-y",
                "-v", "error",
                "-progress", "pipe:1",
                "-i", fullPath,
                "-map", "0:a:0",
                "-ac", "1",
                "-ar", "22050",
                "-c:a", "pcm_s16le",
                tempOutputPath
            };

            var progressParser = new FfmpegProgressParser(metadata.DurationSeconds);

            var conversionResult = await _processRunner.RunProcessAsync(
                tools.FfmpegPath!,
                ffmpegArgs,
                onStdOutLine: line =>
                {
                    var p = progressParser.ParseLine(line);
                    if (p.HasValue) progress?.Report(p.Value);
                },
                ct: ct);

            if (conversionResult.IsCancelled || ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }

            if (!conversionResult.IsSuccess || !File.Exists(tempOutputPath))
            {
                string err = !string.IsNullOrWhiteSpace(conversionResult.StandardError)
                    ? $"{AudioError.ConversionFailed}: {TruncateDiagnostic(conversionResult.StandardError)}"
                    : AudioError.ConversionFailed;
                return AudioIngestResult.Failed(fullPath, err, "CONVERSION_FAILED", request.JobId, metadata);
            }

            ct.ThrowIfCancellationRequested();

            // 7. Atomic rename temp -> final normalized artifact
            string finalNormalizedPath;
            try
            {
                finalNormalizedPath = workspace.CommitNormalizedFile(request.JobId);
            }
            catch (Exception ex)
            {
                return AudioIngestResult.Failed(fullPath, $"파일 저장 실패: {ex.Message}", "FILE_SAVE_ERROR", request.JobId, metadata);
            }

            ct.ThrowIfCancellationRequested();

            // 8. Post-conversion validation of normalized output
            var outputProbe = await _metadataReader.ProbeFileAsync(tools.FfprobePath!, finalNormalizedPath, ct);
            if (!outputProbe.IsValid || outputProbe.Metadata == null ||
                outputProbe.Metadata.Channels != 1 ||
                outputProbe.Metadata.SampleRate != 22050 ||
                !outputProbe.Metadata.CodecName.StartsWith("pcm_s16", StringComparison.OrdinalIgnoreCase) ||
                outputProbe.Metadata.FileSizeBytes <= 0)
            {
                return AudioIngestResult.Failed(fullPath, AudioError.OutputValidationFailed, "OUTPUT_VALIDATION_FAILED", request.JobId, metadata);
            }

            ct.ThrowIfCancellationRequested();

            progress?.Report(1.0);
            committedSuccess = true;

            return AudioIngestResult.Successful(
                request.JobId,
                fullPath,
                finalNormalizedPath,
                metadata);
        }
        catch (OperationCanceledException)
        {
            workspace.CleanJob(request.JobId);
            throw;
        }
        catch (Exception ex)
        {
            workspace.CleanJob(request.JobId);
            return AudioIngestResult.Failed(fullPath, $"{AudioError.ConversionFailed}: {TruncateDiagnostic(ex.Message)}", "CONVERSION_FAILED", request.JobId, metadata);
        }
        finally
        {
            if (!committedSuccess)
            {
                workspace.CleanJob(request.JobId);
            }
        }
    }

    public async Task<IReadOnlyList<AudioIngestResult>> IngestBatchAsync(
        IReadOnlyList<AudioIngestRequest> requests,
        IProgress<(int Current, int Total, string FileName, double Progress)>? progress = null,
        CancellationToken ct = default)
    {
        var results = new List<AudioIngestResult>();
        int total = requests.Count;

        for (int i = 0; i < total; i++)
        {
            var req = requests[i];
            string fname = Path.GetFileName(req.FilePath);

            if (ct.IsCancellationRequested)
            {
                for (int j = i; j < total; j++)
                {
                    results.Add(AudioIngestResult.Failed(requests[j].FilePath, AudioError.Cancelled, "CANCELLED", requests[j].JobId));
                }
                return results;
            }

            progress?.Report((i + 1, total, fname, 0.0));

            var itemProgress = new Progress<double>(p =>
            {
                progress?.Report((i + 1, total, fname, p));
            });

            try
            {
                var result = await IngestAudioAsync(req, itemProgress, ct);
                results.Add(result);
            }
            catch (OperationCanceledException)
            {
                for (int j = i; j < total; j++)
                {
                    results.Add(AudioIngestResult.Failed(requests[j].FilePath, AudioError.Cancelled, "CANCELLED", requests[j].JobId));
                }
                return results;
            }
            catch (Exception ex)
            {
                results.Add(AudioIngestResult.Failed(req.FilePath, $"처리 중 오류 발생: {ex.Message}", "UNEXPECTED_ERROR", req.JobId));
            }
        }

        return results;
    }

    private static string TruncateDiagnostic(string? text, int maxLength = 2048)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        text = text.Trim();
        return text.Length <= maxLength ? text : text[..maxLength] + "... [진단 로그 일부 생략]";
    }
}
