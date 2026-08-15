using System.Text;
using System.Text.Json;
using RobloxPiano.Core.Audio;
using RobloxPiano.Core.YouTube;
using RobloxPiano.Infrastructure.Audio;

namespace RobloxPiano.Infrastructure.YouTube;

public class YouTubeIngestionService : IYouTubeIngestionService
{
    private readonly IYtDlpToolLocator _toolLocator;
    private readonly IYtDlpProcessRunner _processRunner;
    private readonly IAudioIngestionService _audioIngestionService;
    private readonly IFfmpegToolLocator _ffmpegLocator;
    private readonly YouTubeWorkspaceService _workspaceService;
    private readonly TimeSpan _metadataTimeout;

    public YouTubeIngestionService(
        IYtDlpToolLocator? toolLocator = null,
        IYtDlpProcessRunner? processRunner = null,
        IAudioIngestionService? audioIngestionService = null,
        IFfmpegToolLocator? ffmpegLocator = null,
        YouTubeWorkspaceService? workspaceService = null,
        TimeSpan? metadataTimeout = null)
    {
        _toolLocator = toolLocator ?? new YtDlpToolLocator();
        _processRunner = processRunner ?? new YtDlpProcessRunner();
        _audioIngestionService = audioIngestionService ?? new AudioIngestionService();
        _ffmpegLocator = ffmpegLocator ?? new FfmpegToolLocator();
        _workspaceService = workspaceService ?? new YouTubeWorkspaceService();
        _metadataTimeout = metadataTimeout ?? TimeSpan.FromSeconds(30);
    }

    public async Task<YouTubeToolStatus> CheckToolStatusAsync(CancellationToken ct = default)
    {
        var ytDlpStatus = await _toolLocator.LocateAsync(ct: ct);
        if (!ytDlpStatus.IsAvailable)
        {
            return ytDlpStatus;
        }

        var ffmpegInfo = await _ffmpegLocator.LocateToolsAsync(ct: ct);
        if (!ffmpegInfo.IsFullyAvailable)
        {
            return YouTubeToolStatus.Unavailable(YouTubeError.FfmpegUnavailable);
        }

        return ytDlpStatus;
    }

    public async Task<YouTubeMetadata> ProbeMetadataAsync(string url, CancellationToken ct = default)
    {
        var valRes = YouTubeUrlValidator.Validate(url);
        if (!valRes.IsValid)
        {
            if (valRes.IsPlaylistOnly)
            {
                throw new ArgumentException(YouTubeError.PlaylistUnsupported, nameof(url));
            }
            throw new ArgumentException(valRes.ErrorMessage ?? YouTubeError.InvalidUrl, nameof(url));
        }

        var toolStatus = await _toolLocator.LocateAsync(ct: ct);
        if (!toolStatus.IsAvailable || string.IsNullOrEmpty(toolStatus.ExecutablePath))
        {
            throw new InvalidOperationException(toolStatus.StatusMessage);
        }

        var args = new List<string>
        {
            "--ignore-config",
            "--no-playlist",
            "--dump-single-json",
            "--skip-download",
            valRes.CanonicalUrl!
        };

        var res = await _processRunner.RunProcessAsync(
            toolStatus.ExecutablePath,
            args,
            timeout: _metadataTimeout,
            ct: ct
        );

        if (res.IsTimedOut)
        {
            throw new TimeoutException(YouTubeError.MetadataTimeout);
        }

        if (res.IsCancelled || ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(YouTubeError.Cancelled);
        }

        if (!res.IsSuccess)
        {
            string stderr = res.StandardError ?? string.Empty;
            if (stderr.Contains("Sign in", StringComparison.OrdinalIgnoreCase) ||
                stderr.Contains("Private video", StringComparison.OrdinalIgnoreCase) ||
                stderr.Contains("members-only", StringComparison.OrdinalIgnoreCase) ||
                stderr.Contains("login", StringComparison.OrdinalIgnoreCase) ||
                stderr.Contains("requires authentication", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(YouTubeError.LoginRequired);
            }

            throw new InvalidOperationException($"{YouTubeError.MetadataFailed} ({TruncateDiagnostic(stderr)})");
        }

        try
        {
            using var doc = JsonDocument.Parse(res.StandardOutput);
            var root = doc.RootElement;

            string id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
            if (!string.Equals(id, valRes.VideoId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(YouTubeError.VideoIdMismatch);
            }

            if (root.TryGetProperty("is_live", out var liveProp) && liveProp.ValueKind == JsonValueKind.True)
            {
                throw new InvalidOperationException(YouTubeError.LiveUnsupported);
            }

            if (root.TryGetProperty("live_status", out var liveStatusProp))
            {
                string? statusStr = liveStatusProp.GetString();
                if (string.Equals(statusStr, "is_live", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(statusStr, "is_upcoming", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(statusStr, "post_live", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(YouTubeError.LiveUnsupported);
                }
            }

            if (!root.TryGetProperty("duration", out var durProp) ||
                durProp.ValueKind == JsonValueKind.Null ||
                !durProp.TryGetDouble(out double duration) ||
                double.IsNaN(duration) ||
                double.IsInfinity(duration) ||
                duration <= 0)
            {
                throw new InvalidOperationException(YouTubeError.DurationUnknown);
            }

            if (duration > 1800)
            {
                throw new InvalidOperationException(YouTubeError.TooLong);
            }

            string title = root.TryGetProperty("title", out var tProp) ? tProp.GetString() ?? id : id;
            string channel = root.TryGetProperty("channel", out var cProp)
                ? cProp.GetString() ?? "YouTube"
                : (root.TryGetProperty("uploader", out var uProp) ? uProp.GetString() ?? "YouTube" : "YouTube");
            string webpageUrl = root.TryGetProperty("webpage_url", out var wProp)
                ? wProp.GetString() ?? valRes.CanonicalUrl!
                : valRes.CanonicalUrl!;
            string? thumbnail = root.TryGetProperty("thumbnail", out var thProp) ? thProp.GetString() : null;
            string? extractor = root.TryGetProperty("extractor", out var exProp) ? exProp.GetString() : null;

            return new YouTubeMetadata(
                id,
                title,
                duration,
                channel,
                webpageUrl,
                thumbnail,
                false,
                null,
                extractor
            );
        }
        catch (Exception ex) when (ex is not InvalidOperationException && ex is not ArgumentException)
        {
            throw new InvalidOperationException($"{YouTubeError.MetadataFailed}: JSON 파싱 실패 ({ex.Message})", ex);
        }
    }

    public async Task<YouTubeIngestResult> IngestYouTubeAsync(
        YouTubeIngestRequest request,
        IProgress<YouTubeDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var valRes = YouTubeUrlValidator.Validate(request.Url);
        if (!valRes.IsValid)
        {
            if (valRes.IsPlaylistOnly)
            {
                return YouTubeIngestResult.Failed(request.JobId, YouTubeError.PlaylistUnsupported, "PLAYLIST_UNSUPPORTED", null, request.Url);
            }
            return YouTubeIngestResult.Failed(request.JobId, valRes.ErrorMessage ?? YouTubeError.InvalidUrl, "INVALID_URL", null, request.Url);
        }

        var toolStatus = await CheckToolStatusAsync(ct);
        if (!toolStatus.IsAvailable || string.IsNullOrEmpty(toolStatus.ExecutablePath))
        {
            return YouTubeIngestResult.Failed(request.JobId, toolStatus.StatusMessage, "TOOL_UNAVAILABLE", valRes.VideoId, request.Url, valRes.CanonicalUrl);
        }

        YouTubeMetadata metadata;
        try
        {
            progress?.Report(new YouTubeDownloadProgress("영상 정보 확인 중", null, "영상 정보를 확인하고 있습니다..."));
            metadata = await ProbeMetadataAsync(request.Url, ct);
        }
        catch (OperationCanceledException)
        {
            return YouTubeIngestResult.Cancelled(request.JobId, valRes.VideoId, request.Url, valRes.CanonicalUrl);
        }
        catch (Exception ex)
        {
            return YouTubeIngestResult.Failed(
                request.JobId,
                ex.Message,
                "METADATA_ERROR",
                valRes.VideoId,
                request.Url,
                valRes.CanonicalUrl
            );
        }

        string jobDir = _workspaceService.GetJobDirectory(request.JobId);
        string outputTemplate = _workspaceService.GetOutputTemplate(request.JobId);
        string expectedSourceWav = _workspaceService.GetSourceWavPath(request.JobId);

        var ffmpegInfo = await _ffmpegLocator.LocateToolsAsync(ct: ct);
        string? ffmpegLocation = ffmpegInfo.FfmpegPath;

        var downloadArgs = new List<string>
        {
            "--ignore-config",
            "--no-playlist",
            "-f", "bestaudio/best",
            "--extract-audio",
            "--audio-format", "wav",
            "--max-filesize", "500M",
            "--socket-timeout", "15",
            "--retries", "3",
            "--fragment-retries", "3",
            "--newline",
            "--progress-template", "RP_YTDLP_PROGRESS|%(progress.downloaded_bytes)s|%(progress.total_bytes)s|%(progress.total_bytes_estimate)s|%(progress.speed)s|%(progress.eta)s"
        };

        if (!string.IsNullOrEmpty(ffmpegLocation))
        {
            string ffmpegDir = Path.GetDirectoryName(ffmpegLocation) ?? ffmpegLocation;
            downloadArgs.Add("--ffmpeg-location");
            downloadArgs.Add(ffmpegDir);
        }

        downloadArgs.Add("-o");
        downloadArgs.Add(outputTemplate);
        downloadArgs.Add(valRes.CanonicalUrl!);

        progress?.Report(new YouTubeDownloadProgress("다운로드 중", 0, "오디오 다운로드를 시작합니다..."));

        Action<string> onStdOut = line =>
        {
            if (line.StartsWith("RP_YTDLP_PROGRESS|"))
            {
                var parts = line.Split('|');
                long? downloaded = parts.Length > 1 && long.TryParse(parts[1], out var d) ? d : null;
                long? total = parts.Length > 2 && long.TryParse(parts[2], out var t) ? t : null;
                long? totalEst = parts.Length > 3 && long.TryParse(parts[3], out var te) ? te : null;
                double? speed = parts.Length > 4 && double.TryParse(parts[4], out var s) ? s : null;
                double? eta = parts.Length > 5 && double.TryParse(parts[5], out var e) ? e : null;

                long? effectiveTotal = total ?? totalEst;
                double? percent = null;
                if (effectiveTotal.HasValue && effectiveTotal.Value > 0 && downloaded.HasValue)
                {
                    percent = Math.Clamp((double)downloaded.Value / effectiveTotal.Value, 0, 1.0);
                }

                string msg = percent.HasValue
                    ? $"다운로드 중 ({percent.Value * 100:0}%)"
                    : "다운로드 중...";

                progress?.Report(new YouTubeDownloadProgress("다운로드 중", percent, msg, downloaded, effectiveTotal, speed, eta));
            }
        };

        var downloadRes = await _processRunner.RunProcessAsync(
            toolStatus.ExecutablePath,
            downloadArgs,
            onStdOutLine: onStdOut,
            ct: ct
        );

        if (downloadRes.IsCancelled || ct.IsCancellationRequested)
        {
            _workspaceService.CleanJob(request.JobId);
            return YouTubeIngestResult.Cancelled(request.JobId, valRes.VideoId, request.Url, valRes.CanonicalUrl);
        }

        if (!downloadRes.IsSuccess)
        {
            _workspaceService.CleanJob(request.JobId);
            string err = TruncateDiagnostic(downloadRes.StandardError);
            return YouTubeIngestResult.Failed(
                request.JobId,
                $"{YouTubeError.DownloadFailed} ({err})",
                "DOWNLOAD_ERROR",
                valRes.VideoId,
                request.Url,
                valRes.CanonicalUrl,
                metadata.Title,
                metadata.Channel,
                metadata.DurationSeconds,
                metadata.ThumbnailUrl
            );
        }

        if (!File.Exists(expectedSourceWav) || new FileInfo(expectedSourceWav).Length == 0)
        {
            _workspaceService.CleanJob(request.JobId);
            return YouTubeIngestResult.Failed(
                request.JobId,
                YouTubeError.DownloadOutputMissing,
                "OUTPUT_MISSING",
                valRes.VideoId,
                request.Url,
                valRes.CanonicalUrl,
                metadata.Title,
                metadata.Channel,
                metadata.DurationSeconds,
                metadata.ThumbnailUrl
            );
        }

        long fileLen = new FileInfo(expectedSourceWav).Length;
        if (fileLen > 500 * 1024 * 1024)
        {
            _workspaceService.CleanJob(request.JobId);
            return YouTubeIngestResult.Failed(
                request.JobId,
                YouTubeError.FileTooLarge,
                "FILE_TOO_LARGE",
                valRes.VideoId,
                request.Url,
                valRes.CanonicalUrl,
                metadata.Title,
                metadata.Channel,
                metadata.DurationSeconds,
                metadata.ThumbnailUrl
            );
        }

        // Handoff to EXISTING Phase 7 Audio Ingestion Service
        progress?.Report(new YouTubeDownloadProgress("오디오 준비 중", null, "오디오 정규화 처리 중..."));

        var audioReq = new AudioIngestRequest(expectedSourceWav);
        var audioProgress = new Progress<double>(p =>
        {
            progress?.Report(new YouTubeDownloadProgress("오디오 준비 중", p, "오디오 정규화 중..."));
        });

        AudioIngestResult audioRes;
        try
        {
            audioRes = await _audioIngestionService.IngestAudioAsync(audioReq, audioProgress, ct);
        }
        catch (OperationCanceledException)
        {
            _workspaceService.CleanJob(request.JobId);
            return YouTubeIngestResult.Cancelled(request.JobId, valRes.VideoId, request.Url, valRes.CanonicalUrl);
        }
        catch (Exception ex)
        {
            _workspaceService.CleanJob(request.JobId);
            return YouTubeIngestResult.Failed(
                request.JobId,
                $"{YouTubeError.AudioPreparationFailed}: {ex.Message}",
                "AUDIO_INGEST_ERROR",
                valRes.VideoId,
                request.Url,
                valRes.CanonicalUrl,
                metadata.Title,
                metadata.Channel,
                metadata.DurationSeconds,
                metadata.ThumbnailUrl
            );
        }

        if (ct.IsCancellationRequested || audioRes.ErrorCode == "CANCELLED")
        {
            _workspaceService.CleanJob(request.JobId);
            return YouTubeIngestResult.Cancelled(request.JobId, valRes.VideoId, request.Url, valRes.CanonicalUrl);
        }

        if (!audioRes.Success || string.IsNullOrEmpty(audioRes.NormalizedAudioPath))
        {
            _workspaceService.CleanJob(request.JobId);
            return YouTubeIngestResult.Failed(
                request.JobId,
                $"{YouTubeError.AudioPreparationFailed}: {audioRes.ErrorMessage}",
                "AUDIO_INGEST_ERROR",
                valRes.VideoId,
                request.Url,
                valRes.CanonicalUrl,
                metadata.Title,
                metadata.Channel,
                metadata.DurationSeconds,
                metadata.ThumbnailUrl
            );
        }

        // Clean the temporary YouTube job workspace, retaining Phase 7 normalized WAV in AudioWorkspace
        _workspaceService.CleanJob(request.JobId);

        progress?.Report(new YouTubeDownloadProgress("준비 완료", 1.0, "오디오 준비가 완료되었습니다."));

        return YouTubeIngestResult.Successful(
            request.JobId,
            valRes.VideoId!,
            request.Url,
            valRes.CanonicalUrl!,
            metadata.Title,
            metadata.Channel,
            metadata.DurationSeconds,
            metadata.ThumbnailUrl,
            audioRes.NormalizedAudioPath!,
            audioRes
        );
    }

    private static string TruncateDiagnostic(string? text, int maxChars = 1024)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        string trimmed = text.Trim();
        if (trimmed.Length <= maxChars) return trimmed;
        return trimmed.Substring(0, maxChars) + "...";
    }
}
