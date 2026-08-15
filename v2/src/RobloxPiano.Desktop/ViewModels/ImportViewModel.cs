using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobloxPiano.Core.Importing;
using RobloxPiano.Core.Library;
using RobloxPiano.Core.Services;
using RobloxPiano.Infrastructure.Data;

namespace RobloxPiano.Desktop.ViewModels;

public partial class ImportViewModel : ObservableObject, IDisposable
{
    private readonly IImportPipeline _pipeline;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    [ObservableProperty]
    private ObservableCollection<ImportQueueItemViewModel> _queueItems = new();

    [ObservableProperty]
    private bool _isImporting;

    [ObservableProperty]
    private bool _hasItems;

    [ObservableProperty]
    private string _progressStatusText = "대기 중";

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    public event EventHandler<ScoreItem>? OpenScoreRequested;
    public event EventHandler? ViewLibraryRequested;
    public event EventHandler? ScoreImported;

    public ImportViewModel(IImportPipeline? pipeline = null)
    {
        if (pipeline != null)
        {
            _pipeline = pipeline;
        }
        else
        {
            var dbPath = LibraryDatabasePathProvider.GetDefaultDatabasePath();
            var storageRoot = LibraryDatabasePathProvider.GetDefaultLibraryStorageRoot();
            var repository = new SqliteLibraryRepository(dbPath);
            var fileService = new LibraryFileService(storageRoot);
            var folderService = new FolderService(repository, fileService);
            var libraryService = new LibraryService(repository, fileService, folderService);
            _pipeline = new ImportPipeline(libraryService, repository);
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

            QueueItems.Add(new ImportQueueItemViewModel(normalized));
            addedAny = true;
        }

        if (addedAny)
        {
            HasItems = QueueItems.Count > 0;
            SummaryText = $"{QueueItems.Count}개 파일 대기 중";
        }
    }

    [RelayCommand]
    private void BrowseFiles()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "지원 악보 파일 (*.mid;*.midi;*.mml;*.txt)|*.mid;*.midi;*.mml;*.txt|모든 파일 (*.*)|*.*",
            Multiselect = true,
            Title = "악보 파일 가져오기"
        };

        if (dialog.ShowDialog() == true)
        {
            AddFiles(dialog.FileNames);
        }
    }

    [RelayCommand]
    public async Task StartImportAsync()
    {
        if (IsImporting || QueueItems.Count == 0) return;

        IsImporting = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        int total = QueueItems.Count;
        int completed = 0;
        int success = 0;
        int failed = 0;

        ProgressPercent = 0;
        ProgressStatusText = $"0 / {total} 가져오는 중...";

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

                item.SetImporting();
                ProgressStatusText = $"{i + 1} / {total} 가져오는 중: {item.FileName}";

                var req = new ImportRequest(item.FilePath, addToLibrary: true);
                var result = await _pipeline.ImportFileAsync(req, ct);

                if (result.Success)
                {
                    item.SetCompleted(result);
                    success++;
                    ScoreImported?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    item.SetFailed(result.ErrorMessage ?? ImportError.UnsupportedFormat);
                    failed++;
                }

                completed++;
                ProgressPercent = (double)completed / total * 100.0;
            }

            if (ct.IsCancellationRequested)
            {
                SummaryText = $"가져오기 취소됨 ({success}개 성공 · {failed}개 실패)";
                ProgressStatusText = "취소됨";
            }
            else
            {
                SummaryText = $"{success}개 가져오기 완료 · {failed}개 실패";
                ProgressStatusText = "완료됨";
            }
        }
        catch (OperationCanceledException)
        {
            SummaryText = "가져오기가 취소되었습니다.";
            ProgressStatusText = "취소됨";
        }
        finally
        {
            IsImporting = false;
        }
    }

    [RelayCommand]
    public void CancelImport()
    {
        if (!IsImporting) return;
        _cts?.Cancel();
    }

    [RelayCommand]
    public void ClearQueue()
    {
        if (IsImporting) return;
        QueueItems.Clear();
        HasItems = false;
        ProgressPercent = 0;
        ProgressStatusText = "대기 중";
        SummaryText = string.Empty;
    }

    [RelayCommand]
    public void OpenInPlayer(ImportQueueItemViewModel? item)
    {
        if (item == null) return;

        if (item.CreatedScore != null)
        {
            OpenScoreRequested?.Invoke(this, item.CreatedScore);
        }
        else if (item.Result?.Timeline != null)
        {
            var fallbackScore = new ScoreItem(
                id: Guid.NewGuid().ToString(),
                title: item.Result.Title,
                sourceType: item.Result.SourceType.ToString().ToUpperInvariant(),
                sourceUrl: item.FilePath,
                filePath: item.FilePath,
                duration: item.Result.Duration,
                bpm: item.Result.InitialBpm,
                totalNotes: item.Result.NoteCount
            );
            OpenScoreRequested?.Invoke(this, fallbackScore);
        }
    }

    [RelayCommand]
    public void ViewInLibrary(ImportQueueItemViewModel? item)
    {
        ViewLibraryRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
