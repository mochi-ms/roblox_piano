using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobloxPiano.Core.Importing;
using RobloxPiano.Core.Library;
using RobloxPiano.Core.Piano;
using RobloxPiano.Core.Services;
using RobloxPiano.Infrastructure.Data;

namespace RobloxPiano.Desktop.ViewModels;

public partial class ImportViewModel : ObservableObject, IDisposable
{
    private readonly IImportPipeline _pipeline;
    private readonly PianoProfileContext _profileContext;
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

    [ObservableProperty]
    private int _selectedModeIndex = 0; // 0 = File, 1 = Text

    [ObservableProperty]
    private string _pastedMmlText = string.Empty;

    [ObservableProperty]
    private string _pastedMmlTitle = string.Empty;

    [ObservableProperty]
    private string _textImportErrorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasTextImportError;

    [ObservableProperty]
    private bool _isTextImporting;

    public bool IsFileMode => SelectedModeIndex == 0;
    public bool IsTextMode => SelectedModeIndex == 1;

    partial void OnSelectedModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsFileMode));
        OnPropertyChanged(nameof(IsTextMode));
    }

    public event EventHandler<ScoreItem>? OpenScoreRequested;
    public event EventHandler? ViewLibraryRequested;
    public event EventHandler? ScoreImported;

    public PianoProfileContext ProfileContext => _profileContext;

    public ImportViewModel() : this(null, null)
    {
    }

    public ImportViewModel(IImportPipeline? pipeline = null, PianoProfileContext? profileContext = null)
    {
        _profileContext = profileContext ?? new PianoProfileContext();
        _profileContext.ProfileChanged += OnProfileChanged;

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

    private void OnProfileChanged(object? sender, PianoProfile newProfile)
    {
        foreach (var item in QueueItems)
        {
            if (item.Result?.Timeline != null)
            {
                var validation = ImportTimelineValidator.Validate(item.Result.Timeline, newProfile);
                item.UpdateDiagnostics(validation.PlayableNotes, validation.OutOfRangeNotes);
            }
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

        // Clean up previous CTS and create a new one
        _cts?.Dispose();
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

                var req = new ImportRequest(item.FilePath, addToLibrary: true, targetPianoProfile: _profileContext.CurrentProfile);

                ImportResult result;
                try
                {
                    // Offload detection and synchronous parser work from UI dispatcher thread
                    result = await Task.Run(() => _pipeline.ImportFileAsync(req, ct), ct);
                }
                catch (OperationCanceledException)
                {
                    item.SetCancelled();
                    // Mark all remaining pending items as cancelled
                    for (int j = i + 1; j < total; j++)
                    {
                        if (QueueItems[j].Status == ImportItemStatus.Pending || QueueItems[j].Status == ImportItemStatus.Importing)
                        {
                            QueueItems[j].SetCancelled();
                        }
                    }
                    SummaryText = $"가져오기가 취소되었습니다. ({success}개 완료 · {failed}개 실패)";
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
            // Outer catch if cancellation happened before loop entry
            for (int i = 0; i < total; i++)
            {
                if (QueueItems[i].Status == ImportItemStatus.Pending || QueueItems[i].Status == ImportItemStatus.Importing)
                {
                    QueueItems[i].SetCancelled();
                }
            }
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
    public void RemoveItem(ImportQueueItemViewModel? item)
    {
        if (item == null || IsImporting) return;
        QueueItems.Remove(item);
        HasItems = QueueItems.Count > 0;
        SummaryText = HasItems ? $"{QueueItems.Count}개 파일 대기 중" : string.Empty;
    }

    [RelayCommand]
    public void ClearCompleted()
    {
        if (IsImporting) return;
        var toRemove = QueueItems.Where(q => q.Status == ImportItemStatus.Completed || q.Status == ImportItemStatus.Failed || q.Status == ImportItemStatus.Cancelled).ToList();
        foreach (var item in toRemove)
        {
            QueueItems.Remove(item);
        }
        HasItems = QueueItems.Count > 0;
        SummaryText = HasItems ? $"{QueueItems.Count}개 파일 대기 중" : string.Empty;
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

    [RelayCommand]
    public void SelectMode(string mode)
    {
        SelectedModeIndex = mode == "Text" ? 1 : 0;
    }

    [RelayCommand]
    public async Task ImportPastedTextAsync()
    {
        if (IsTextImporting || string.IsNullOrWhiteSpace(PastedMmlText))
            return;

        IsTextImporting = true;
        HasTextImportError = false;
        TextImportErrorMessage = string.Empty;

        try
        {
            var result = await _pipeline.ImportTextAsync(
                PastedMmlText.Trim(),
                string.IsNullOrWhiteSpace(PastedMmlTitle) ? null : PastedMmlTitle.Trim(),
                addToLibrary: true,
                targetPianoProfile: _profileContext.CurrentProfile);

            if (result.Success)
            {
                var queueItem = new ImportQueueItemViewModel(result.FilePath);
                queueItem.SetCompleted(result);
                QueueItems.Insert(0, queueItem);
                HasItems = true;
                SummaryText = $"'{result.Title}' 가져오기 완료";
                ScoreImported?.Invoke(this, EventArgs.Empty);

                SelectedModeIndex = 0;
                PastedMmlText = string.Empty;
                PastedMmlTitle = string.Empty;
            }
            else
            {
                HasTextImportError = true;
                TextImportErrorMessage = result.ErrorMessage ?? "MML 파싱에 실패했습니다.";
            }
        }
        catch (Exception ex)
        {
            HasTextImportError = true;
            TextImportErrorMessage = $"오류: {ex.Message}";
        }
        finally
        {
            IsTextImporting = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _profileContext.ProfileChanged -= OnProfileChanged;
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
