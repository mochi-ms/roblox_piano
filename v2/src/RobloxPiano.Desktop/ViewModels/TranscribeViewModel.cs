using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobloxPiano.Core.Audio;
using RobloxPiano.Core.Importing;
using RobloxPiano.Core.Music;
using RobloxPiano.Core.Piano;
using RobloxPiano.Core.Transcription;
using RobloxPiano.Core.YouTube;
using RobloxPiano.Infrastructure.Audio;
using RobloxPiano.Infrastructure.Transcription;
using RobloxPiano.Infrastructure.YouTube;

namespace RobloxPiano.Desktop.ViewModels;

public partial class TranscribeViewModel : ObservableObject, IDisposable
{
    private readonly IAudioIngestionService _ingestionService;
    private readonly IFfmpegToolLocator _toolLocator;
    private readonly ITranscriptionEngine _transcriptionEngine;
    private readonly IImportPipeline _importPipeline;
    private readonly PianoProfileContext _profileContext;
    private readonly IYouTubeIngestionService _youtubeService;

    private CancellationTokenSource? _audioCts;
    private CancellationTokenSource? _aiCts;
    private bool _disposed;

    public event EventHandler<MusicTimeline>? OpenScoreRequested;
    public event EventHandler? ScoreImported;

    public PianoProfileContext ProfileContext => _profileContext;

    [ObservableProperty]
    private ObservableCollection<AudioQueueItemViewModel> _queueItems = new();

    [ObservableProperty]
    private string _youTubeUrlInput = string.Empty;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private bool _isAiProcessing;

    [ObservableProperty]
    private bool _hasItems;

    [ObservableProperty]
    private bool _hasPreparedItems;

    [ObservableProperty]
    private string _progressStatusText = "대기 중";

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private bool _isFfmpegReady;

    [ObservableProperty]
    private string _ffmpegStatusText = "FFmpeg 확인 중...";

    [ObservableProperty]
    private bool _isYtDlpReady;

    [ObservableProperty]
    private string _ytDlpStatusText = "yt-dlp 확인 중...";

    [ObservableProperty]
    private bool _isAiEngineReady;

    [ObservableProperty]
    private string _aiEngineStatusText = "AI 엔진 확인 중...";

    public TranscribeViewModel() : this(null, null, null, null, null, null)
    {
    }

    public TranscribeViewModel(
        IAudioIngestionService? ingestionService = null,
        IFfmpegToolLocator? toolLocator = null,
        ITranscriptionEngine? transcriptionEngine = null,
        IImportPipeline? importPipeline = null,
        PianoProfileContext? profileContext = null,
        IYouTubeIngestionService? youtubeService = null)
    {
        _toolLocator = toolLocator ?? new FfmpegToolLocator();
        _ingestionService = ingestionService ?? new AudioIngestionService(_toolLocator);
        _transcriptionEngine = transcriptionEngine ?? new PythonBasicPitchTranscriptionEngine();
        _importPipeline = importPipeline ?? new ImportPipeline();
        _profileContext = profileContext ?? new PianoProfileContext();
        _youtubeService = youtubeService ?? new YouTubeIngestionService(audioIngestionService: _ingestionService, ffmpegLocator: _toolLocator);

        _profileContext.ProfileChanged += OnProfileChanged;

        _ = CheckToolsAsync();
    }

    private void OnProfileChanged(object? sender, PianoProfile newProfile)
    {
        foreach (var item in QueueItems)
        {
            if (item.AiResult?.Timeline != null)
            {
                var validation = ImportTimelineValidator.Validate(item.AiResult.Timeline, newProfile);
                item.UpdateDiagnostics(validation.PlayableNotes, validation.OutOfRangeNotes);
            }
        }
    }

    public async Task CheckToolsAsync()
    {
        // 1. Check FFmpeg
        try
        {
            var tools = await _toolLocator.LocateToolsAsync();
            if (tools.IsFullyAvailable)
            {
                IsFfmpegReady = true;
                FfmpegStatusText = $"● FFmpeg 사용 가능 ({tools.FfmpegVersionLine ?? "정상"})";
            }
            else if (tools.IsFfmpegAvailable && !tools.IsFfprobeAvailable)
            {
                IsFfmpegReady = false;
                FfmpegStatusText = "▲ FFprobe를 찾을 수 없습니다.";
            }
            else
            {
                IsFfmpegReady = false;
                FfmpegStatusText = "▲ FFmpeg/FFprobe를 찾을 수 없습니다.";
            }
        }
        catch
        {
            IsFfmpegReady = false;
            FfmpegStatusText = "▲ FFmpeg 확인 실패";
        }

        // 2. Check yt-dlp
        try
        {
            var ytStatus = await _youtubeService.CheckToolStatusAsync();
            IsYtDlpReady = ytStatus.IsAvailable;
            YtDlpStatusText = ytStatus.StatusMessage;
        }
        catch
        {
            IsYtDlpReady = false;
            YtDlpStatusText = "▲ yt-dlp 상태 확인 실패";
        }

        // 3. Check AI Engine
        try
        {
            var aiStatus = await _transcriptionEngine.CheckAvailabilityAsync();
            IsAiEngineReady = aiStatus.IsAvailable;
            AiEngineStatusText = aiStatus.StatusMessage;
        }
        catch
        {
            IsAiEngineReady = false;
            AiEngineStatusText = "▲ AI 엔진 상태 확인 실패";
        }
    }

    public void AddFiles(IEnumerable<string> filePaths)
    {
        bool addedAny = false;
        foreach (var path in filePaths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            string normalized = Path.GetFullPath(path);
            if (QueueItems.Any(q => string.Equals(q.FilePath, normalized, StringComparison.OrdinalIgnoreCase)))
                continue;

            QueueItems.Add(new AudioQueueItemViewModel(normalized));
            addedAny = true;
        }

        if (addedAny)
        {
            UpdateItemStates();
            SummaryText = $"{QueueItems.Count}개 항목 대기 중";
        }
    }

    [RelayCommand]
    public void AddYouTubeUrl()
    {
        if (string.IsNullOrWhiteSpace(YouTubeUrlInput)) return;

        var lines = YouTubeUrlInput.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries);
        bool addedAny = false;
        string? lastError = null;

        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var val = YouTubeUrlValidator.Validate(line);
            if (!val.IsValid)
            {
                lastError = val.IsPlaylistOnly ? YouTubeError.PlaylistUnsupported : (val.ErrorMessage ?? YouTubeError.InvalidUrl);
                continue;
            }

            // Duplicate VideoId check within queue
            if (QueueItems.Any(q => q.SourceKind == AudioSourceKind.YouTube && string.Equals(q.VideoId, val.VideoId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var item = AudioQueueItemViewModel.ForYouTube(val.VideoId!, val.OriginalUrl!, val.CanonicalUrl!);
            QueueItems.Add(item);
            addedAny = true;
        }

        if (addedAny)
        {
            YouTubeUrlInput = string.Empty;
            UpdateItemStates();
            SummaryText = $"{QueueItems.Count}개 항목 대기 중";
        }
        else if (lastError != null)
        {
            SummaryText = lastError;
        }
    }

    private void UpdateItemStates()
    {
        HasItems = QueueItems.Count > 0;
        HasPreparedItems = QueueItems.Any(q => q.IsPrepared && !q.IsAiCompleted);
    }

    [RelayCommand]
    private void BrowseFiles()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "오디오 파일 (*.mp3;*.wav;*.m4a;*.flac;*.aac;*.ogg)|*.mp3;*.wav;*.m4a;*.flac;*.aac;*.ogg|모든 파일 (*.*)|*.*",
            Multiselect = true,
            Title = "오디오 파일 불러오기"
        };

        if (dialog.ShowDialog() == true)
        {
            AddFiles(dialog.FileNames);
        }
    }

    [RelayCommand]
    public async Task StartIngestAsync()
    {
        if (IsProcessing || IsAiProcessing || QueueItems.Count == 0) return;

        IsProcessing = true;

        _audioCts?.Dispose();
        _audioCts = new CancellationTokenSource();
        var ct = _audioCts.Token;

        int total = QueueItems.Count;
        int completed = 0;
        int success = 0;
        int failed = 0;

        ProgressPercent = 0;
        ProgressStatusText = $"0 / {total} 오디오 준비 중...";

        try
        {
            for (int i = 0; i < total; i++)
            {
                var item = QueueItems[i];

                if (ct.IsCancellationRequested)
                {
                    item.SetCancelled();
                    continue;
                }

                item.SetProbing();

                if (item.SourceKind == AudioSourceKind.YouTube)
                {
                    // YouTube Download & Ingest
                    ProgressStatusText = $"{i + 1} / {total} YouTube 오디오 가져오는 중: {item.FileName}";

                    var ytReq = new YouTubeIngestRequest(item.JobId, item.CanonicalUrl ?? item.FilePath);
                    var ytProgress = new Progress<YouTubeDownloadProgress>(p =>
                    {
                        item.SetYouTubeDownloading(p.Message, p.Percent);
                        ProgressStatusText = $"{i + 1} / {total} {item.FileName}: {p.Message}";
                    });

                    YouTubeIngestResult ytResult;
                    try
                    {
                        ytResult = await Task.Run(() => _youtubeService.IngestYouTubeAsync(ytReq, ytProgress, ct), ct);
                    }
                    catch (OperationCanceledException)
                    {
                        item.SetCancelled();
                        CancelRemaining(i + 1, total);
                        SummaryText = $"YouTube 가져오기가 취소되었습니다. ({success}개 완료 · {failed}개 실패)";
                        ProgressStatusText = "취소됨";
                        return;
                    }
                    catch (Exception ex)
                    {
                        item.SetFailed($"오류: {ex.Message}");
                        failed++;
                        completed++;
                        ProgressPercent = (double)completed / total * 100.0;
                        continue;
                    }

                    if (ytResult.Success)
                    {
                        item.SetYouTubePrepared(ytResult);
                        success++;
                    }
                    else if (string.Equals(ytResult.ErrorCode, "CANCELLED", StringComparison.OrdinalIgnoreCase) || ct.IsCancellationRequested)
                    {
                        item.SetCancelled();
                        CancelRemaining(i + 1, total);
                        SummaryText = $"오디오 준비가 취소되었습니다. ({success}개 완료 · {failed}개 실패)";
                        ProgressStatusText = "취소됨";
                        return;
                    }
                    else
                    {
                        item.SetFailed(ytResult.ErrorMessage ?? YouTubeError.DownloadFailed);
                        failed++;
                    }
                }
                else
                {
                    // Local Audio File Normalization
                    ProgressStatusText = $"{i + 1} / {total} 변환 중: {item.FileName}";

                    var req = new AudioIngestRequest(item.FilePath, item.JobId);

                    var itemProgress = new Progress<double>(p =>
                    {
                        item.SetConverting(p);
                    });

                    AudioIngestResult result;
                    try
                    {
                        result = await Task.Run(() => _ingestionService.IngestAudioAsync(req, itemProgress, ct), ct);
                    }
                    catch (OperationCanceledException)
                    {
                        item.SetCancelled();
                        CancelRemaining(i + 1, total);
                        SummaryText = $"오디오 준비가 취소되었습니다. ({success}개 완료 · {failed}개 실패)";
                        ProgressStatusText = "취소됨";
                        return;
                    }
                    catch (Exception ex)
                    {
                        item.SetFailed($"오류: {ex.Message}");
                        failed++;
                        completed++;
                        ProgressPercent = (double)completed / total * 100.0;
                        continue;
                    }

                    if (result.Success)
                    {
                        item.SetPrepared(result);
                        success++;
                    }
                    else if (string.Equals(result.ErrorCode, "CANCELLED", StringComparison.OrdinalIgnoreCase) || ct.IsCancellationRequested)
                    {
                        item.SetCancelled();
                        CancelRemaining(i + 1, total);
                        SummaryText = $"오디오 준비가 취소되었습니다. ({success}개 완료 · {failed}개 실패)";
                        ProgressStatusText = "취소됨";
                        return;
                    }
                    else
                    {
                        item.SetFailed(result.ErrorMessage ?? AudioError.InvalidMedia);
                        failed++;
                    }
                }

                completed++;
                ProgressPercent = (double)completed / total * 100.0;
            }

            if (ct.IsCancellationRequested)
            {
                SummaryText = $"오디오 준비 취소됨 ({success}개 성공 · {failed}개 실패)";
                ProgressStatusText = "취소됨";
            }
            else
            {
                SummaryText = $"{success}개 오디오 준비 완료 · {failed}개 실패 ([AI 악보 변환]을 클릭하여 악보를 추출하세요)";
                ProgressStatusText = "준비 완료";
            }
        }
        catch (OperationCanceledException)
        {
            CancelRemaining(0, total);
            SummaryText = "오디오 준비가 취소되었습니다.";
            ProgressStatusText = "취소됨";
        }
        finally
        {
            IsProcessing = false;
            UpdateItemStates();
        }
    }

    private void CancelRemaining(int startIndex, int total)
    {
        for (int j = startIndex; j < total; j++)
        {
            if (QueueItems[j].Status == AudioItemStatus.Pending ||
                QueueItems[j].Status == AudioItemStatus.Probing ||
                QueueItems[j].Status == AudioItemStatus.Converting)
            {
                QueueItems[j].SetCancelled();
            }
        }
    }

    [RelayCommand]
    public void CancelIngest()
    {
        if (!IsProcessing) return;
        _audioCts?.Cancel();
    }

    [RelayCommand]
    public async Task StartAiTranscriptionAsync(AudioQueueItemViewModel? item)
    {
        if (item == null || !item.IsPrepared || IsAiProcessing || IsProcessing) return;

        IsAiProcessing = true;
        item.SetAiStarting();

        _aiCts?.Dispose();
        _aiCts = new CancellationTokenSource();
        var ct = _aiCts.Token;

        ProgressStatusText = $"AI 악보 분석 중: {item.FileName}";

        try
        {
            string title = item.YouTubeTitle ?? item.Result?.Metadata?.Title ?? Path.GetFileNameWithoutExtension(item.FilePath);

            var req = new TranscriptionRequest(
                item.JobId,
                item.NormalizedAudioPath!,
                SourceTitle: title,
                TargetPianoProfile: _profileContext.CurrentProfile
            );

            var progress = new Progress<TranscriptionProgress>(p =>
            {
                item.SetAiAnalyzing(p.Message);
                ProgressStatusText = $"{item.FileName} - {p.Message}";
            });

            var result = await Task.Run(() => _transcriptionEngine.TranscribeAsync(req, progress, ct), ct);

            if (result.Success)
            {
                item.SetAiCompleted(result);
                SummaryText = $"'{item.FileName}' AI 악보 변환 완료 ({result.NoteCount}음 감지, 소요 시간 {result.RuntimeSeconds:F1}초)";
                ProgressStatusText = "분석 완료";
            }
            else if (string.Equals(result.ErrorCode, "CANCELLED", StringComparison.OrdinalIgnoreCase) || ct.IsCancellationRequested)
            {
                item.SetAiCancelled();
                SummaryText = "AI 악보 변환이 취소되었습니다.";
                ProgressStatusText = "취소됨";
            }
            else
            {
                item.SetAiFailed(result.ErrorMessage ?? TranscriptionError.InferenceFailed);
                SummaryText = $"AI 악보 변환 실패: {result.ErrorMessage}";
                ProgressStatusText = "분석 실패";
            }
        }
        catch (OperationCanceledException)
        {
            item.SetAiCancelled();
            SummaryText = "AI 악보 변환이 취소되었습니다.";
            ProgressStatusText = "취소됨";
        }
        catch (Exception ex)
        {
            item.SetAiFailed($"오류: {ex.Message}");
            SummaryText = $"AI 분석 중 오류 발생: {ex.Message}";
            ProgressStatusText = "오류 발생";
        }
        finally
        {
            IsAiProcessing = false;
            UpdateItemStates();
        }
    }

    [RelayCommand]
    public async Task StartBatchAiTranscriptionAsync()
    {
        if (IsAiProcessing || IsProcessing) return;

        var preparedItems = QueueItems.Where(q => q.IsPrepared && !q.IsAiCompleted).ToList();
        if (preparedItems.Count == 0) return;

        IsAiProcessing = true;

        _aiCts?.Dispose();
        _aiCts = new CancellationTokenSource();
        var ct = _aiCts.Token;

        int total = preparedItems.Count;
        int success = 0;
        int failed = 0;

        try
        {
            for (int i = 0; i < total; i++)
            {
                var item = preparedItems[i];

                if (ct.IsCancellationRequested)
                {
                    item.SetAiCancelled();
                    continue;
                }

                item.SetAiStarting();
                ProgressStatusText = $"[{i + 1}/{total}] AI 분석 중: {item.FileName}";

                string title = item.YouTubeTitle ?? item.Result?.Metadata?.Title ?? Path.GetFileNameWithoutExtension(item.FilePath);

                var req = new TranscriptionRequest(
                    item.JobId,
                    item.NormalizedAudioPath!,
                    SourceTitle: title,
                    TargetPianoProfile: _profileContext.CurrentProfile
                );

                var progress = new Progress<TranscriptionProgress>(p =>
                {
                    item.SetAiAnalyzing(p.Message);
                });

                TranscriptionResult result;
                try
                {
                    result = await Task.Run(() => _transcriptionEngine.TranscribeAsync(req, progress, ct), ct);
                }
                catch (OperationCanceledException)
                {
                    item.SetAiCancelled();
                    for (int j = i + 1; j < total; j++)
                    {
                        preparedItems[j].SetAiCancelled();
                    }
                    SummaryText = $"일괄 AI 악보 변환 취소됨 ({success}개 완료 · {failed}개 실패)";
                    ProgressStatusText = "취소됨";
                    return;
                }
                catch (Exception ex)
                {
                    item.SetAiFailed($"오류: {ex.Message}");
                    failed++;
                    continue;
                }

                if (result.Success)
                {
                    item.SetAiCompleted(result);
                    success++;
                }
                else if (string.Equals(result.ErrorCode, "CANCELLED", StringComparison.OrdinalIgnoreCase) || ct.IsCancellationRequested)
                {
                    item.SetAiCancelled();
                    for (int j = i + 1; j < total; j++)
                    {
                        preparedItems[j].SetAiCancelled();
                    }
                    SummaryText = $"일괄 AI 악보 변환 취소됨 ({success}개 완료 · {failed}개 실패)";
                    ProgressStatusText = "취소됨";
                    return;
                }
                else
                {
                    item.SetAiFailed(result.ErrorMessage ?? TranscriptionError.InferenceFailed);
                    failed++;
                }
            }

            SummaryText = $"일괄 AI 악보 변환 완료 ({success}개 성공 · {failed}개 실패)";
            ProgressStatusText = "일괄 분석 완료";
        }
        catch (OperationCanceledException)
        {
            foreach (var item in preparedItems.Where(it => it.IsAiProcessing || it.Status == AudioItemStatus.Prepared))
            {
                item.SetAiCancelled();
            }
            SummaryText = "일괄 AI 악보 변환이 취소되었습니다.";
            ProgressStatusText = "취소됨";
        }
        finally
        {
            IsAiProcessing = false;
            UpdateItemStates();
        }
    }

    [RelayCommand]
    public void CancelAiTranscription()
    {
        if (!IsAiProcessing) return;
        _aiCts?.Cancel();
    }

    [RelayCommand]
    public void OpenInPlayer(AudioQueueItemViewModel? item)
    {
        if (item?.AiResult?.Timeline != null)
        {
            OpenScoreRequested?.Invoke(this, item.AiResult.Timeline);
        }
    }

    [RelayCommand]
    public async Task AddToLibraryAsync(AudioQueueItemViewModel? item)
    {
        if (item?.GeneratedMidiPath == null || !File.Exists(item.GeneratedMidiPath)) return;

        string? title = item.YouTubeTitle ?? item.Result?.Metadata?.Title;
        if (string.IsNullOrWhiteSpace(title))
        {
            title = Path.GetFileNameWithoutExtension(item.FilePath);
        }
        if (string.IsNullOrWhiteSpace(title))
        {
            title = "YouTube AI 변환 악보";
        }

        try
        {
            var req = new ImportRequest(
                item.GeneratedMidiPath,
                title,
                targetFolderId: null,
                addToLibrary: true,
                targetPianoProfile: _profileContext.CurrentProfile
            );

            var res = await _importPipeline.ImportFileAsync(req);
            if (res.Success && res.Timeline != null)
            {
                SummaryText = $"'{title}' 악보가 라이브러리에 저장되었습니다.";
                ScoreImported?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                SummaryText = $"라이브러리 추가 실패: {res.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            SummaryText = $"라이브러리 추가 중 오류: {ex.Message}";
        }
    }

    [RelayCommand]
    public void ClearQueue()
    {
        if (IsProcessing || IsAiProcessing) return;
        QueueItems.Clear();
        HasItems = false;
        HasPreparedItems = false;
        ProgressPercent = 0;
        ProgressStatusText = "대기 중";
        SummaryText = string.Empty;
    }

    [RelayCommand]
    public void OpenWorkspaceFolder(AudioQueueItemViewModel? item)
    {
        string? targetPath = item?.GeneratedMidiPath ?? item?.NormalizedAudioPath;
        if (targetPath != null && File.Exists(targetPath))
        {
            string? dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = dir,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _profileContext.ProfileChanged -= OnProfileChanged;

        _audioCts?.Cancel();
        _audioCts?.Dispose();

        _aiCts?.Cancel();
        _aiCts?.Dispose();

        _transcriptionEngine.Dispose();
    }
}
