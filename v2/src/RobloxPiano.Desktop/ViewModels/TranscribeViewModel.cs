using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobloxPiano.Core.Audio;
using RobloxPiano.Infrastructure.Audio;

namespace RobloxPiano.Desktop.ViewModels;

public partial class TranscribeViewModel : ObservableObject, IDisposable
{
    private readonly IAudioIngestionService _ingestionService;
    private readonly IFfmpegToolLocator _toolLocator;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    [ObservableProperty]
    private ObservableCollection<AudioQueueItemViewModel> _queueItems = new();

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private bool _hasItems;

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

    public TranscribeViewModel(
        IAudioIngestionService? ingestionService = null,
        IFfmpegToolLocator? toolLocator = null)
    {
        _toolLocator = toolLocator ?? new FfmpegToolLocator();
        _ingestionService = ingestionService ?? new AudioIngestionService(_toolLocator);

        _ = CheckToolsAsync();
    }

    public async Task CheckToolsAsync()
    {
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
            HasItems = QueueItems.Count > 0;
            SummaryText = $"{QueueItems.Count}개 오디오 파일 대기 중";
        }
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
        if (IsProcessing || QueueItems.Count == 0) return;

        IsProcessing = true;

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

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
                ProgressStatusText = $"{i + 1} / {total} 처리 중: {item.FileName}";

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
                    for (int j = i + 1; j < total; j++)
                    {
                        if (QueueItems[j].Status == AudioItemStatus.Pending || QueueItems[j].Status == AudioItemStatus.Probing || QueueItems[j].Status == AudioItemStatus.Converting)
                        {
                            QueueItems[j].SetCancelled();
                        }
                    }
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
                else
                {
                    item.SetFailed(result.ErrorMessage ?? AudioError.InvalidMedia);
                    failed++;
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
                SummaryText = $"{success}개 오디오 준비 완료 · {failed}개 실패 (AI 악보 변환 기능은 다음 단계에서 연결됩니다)";
                ProgressStatusText = "준비 완료";
            }
        }
        catch (OperationCanceledException)
        {
            for (int i = 0; i < total; i++)
            {
                if (QueueItems[i].Status == AudioItemStatus.Pending || QueueItems[i].Status == AudioItemStatus.Probing || QueueItems[i].Status == AudioItemStatus.Converting)
                {
                    QueueItems[i].SetCancelled();
                }
            }
            SummaryText = "오디오 준비가 취소되었습니다.";
            ProgressStatusText = "취소됨";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    public void CancelIngest()
    {
        if (!IsProcessing) return;
        _cts?.Cancel();
    }

    [RelayCommand]
    public void ClearQueue()
    {
        if (IsProcessing) return;
        QueueItems.Clear();
        HasItems = false;
        ProgressPercent = 0;
        ProgressStatusText = "대기 중";
        SummaryText = string.Empty;
    }

    [RelayCommand]
    public void OpenWorkspaceFolder(AudioQueueItemViewModel? item)
    {
        if (item?.NormalizedAudioPath != null && File.Exists(item.NormalizedAudioPath))
        {
            string? dir = Path.GetDirectoryName(item.NormalizedAudioPath);
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
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
