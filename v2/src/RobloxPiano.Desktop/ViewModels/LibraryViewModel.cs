using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobloxPiano.Core.Library;
using RobloxPiano.Core.Services;
using RobloxPiano.Infrastructure.Data;

namespace RobloxPiano.Desktop.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly ILibraryRepository _repository;
    private readonly LibraryFileService _fileService;
    private readonly FolderService _folderService;
    private readonly LibraryService _libraryService;

    private CancellationTokenSource? _searchCts;
    private int _queryGeneration = 0;

    private readonly Stack<string?> _backStack = new();
    private readonly Stack<string?> _forwardStack = new();

    private ScoreItemViewModel? _clipboardScore;
    private bool _isCut;

    [ObservableProperty]
    private ObservableCollection<ScoreItemViewModel> _displayedScores = new();

    [ObservableProperty]
    private ObservableCollection<FolderItemViewModel> _folderList = new();

    [ObservableProperty]
    private ScoreItemViewModel? _selectedScore;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string? _currentFolderId;

    [ObservableProperty]
    private string _currentFolderName = "내 라이브러리";

    [ObservableProperty]
    private string _breadcrumbPath = "내 라이브러리";

    [ObservableProperty]
    private bool _isFavoritesView = false;

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private bool _isLoadingMore = false;

    [ObservableProperty]
    private bool _hasMoreItems = false;

    [ObservableProperty]
    private int _currentPageIndex = 0;

    [ObservableProperty]
    private int _pageSize = 100;

    [ObservableProperty]
    private bool _isEmpty = false;

    [ObservableProperty]
    private int _totalItemCount = 0;

    [ObservableProperty]
    private string _statusText = "0개 항목";

    [ObservableProperty]
    private bool _canGoBack = false;

    [ObservableProperty]
    private bool _canGoForward = false;

    [ObservableProperty]
    private bool _canGoUp = false;

    public LibraryViewModel()
    {
        var dbPath = LibraryDatabasePathProvider.GetDefaultDatabasePath();
        var storageRoot = LibraryDatabasePathProvider.GetDefaultLibraryStorageRoot();

        _repository = new SqliteLibraryRepository(dbPath);
        _fileService = new LibraryFileService(storageRoot);
        _folderService = new FolderService(_repository, _fileService);
        _libraryService = new LibraryService(_repository, _fileService, _folderService);

        _ = InitializeAsync();
    }

    public LibraryViewModel(
        ILibraryRepository repository,
        LibraryFileService fileService,
        FolderService folderService,
        LibraryService libraryService)
    {
        _repository = repository;
        _fileService = fileService;
        _folderService = folderService;
        _libraryService = libraryService;

        _ = InitializeAsync();
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            await _repository.InitializeAsync();
            await LoadFoldersAsync();
            await ReloadQueryAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"오류: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            UpdateEmptyState();
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(150, ct); // 150ms debounce
                if (!ct.IsCancellationRequested)
                {
                    await Application.Current.Dispatcher.InvokeAsync(async () =>
                    {
                        await ReloadQueryAsync(ct);
                    });
                }
            }
            catch (TaskCanceledException) { }
        }, ct);
    }

    partial void OnSelectedScoreChanged(ScoreItemViewModel? value)
    {
        UpdateStatusText();
    }

    public async Task LoadFoldersAsync(CancellationToken ct = default)
    {
        var folders = await _repository.GetAllFoldersAsync(ct);
        FolderList.Clear();
        foreach (var f in folders)
        {
            FolderList.Add(new FolderItemViewModel(f));
        }
    }

    [RelayCommand]
    public async Task ReloadQueryAsync(CancellationToken ct = default)
    {
        int gen = Interlocked.Increment(ref _queryGeneration);
        IsLoading = true;
        CurrentPageIndex = 0;

        try
        {
            var query = new LibraryQuery
            {
                FolderId = CurrentFolderId,
                SearchKeyword = SearchText,
                FavoritesOnly = IsFavoritesView,
                PageIndex = 0,
                PageSize = PageSize
            };

            var page = await _repository.QueryScoresAsync(query, ct);
            if (gen != _queryGeneration) return; // Stale query check

            DisplayedScores.Clear();
            foreach (var item in page.Items)
            {
                DisplayedScores.Add(new ScoreItemViewModel(item));
            }

            TotalItemCount = page.TotalCount;
            HasMoreItems = DisplayedScores.Count < TotalItemCount;
            UpdateStatusText();
            UpdateEmptyState();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task LoadNextPageAsync(CancellationToken ct = default)
    {
        if (!HasMoreItems || IsLoadingMore || IsLoading) return;

        int gen = _queryGeneration;
        IsLoadingMore = true;

        try
        {
            int nextPage = CurrentPageIndex + 1;
            var query = new LibraryQuery
            {
                FolderId = CurrentFolderId,
                SearchKeyword = SearchText,
                FavoritesOnly = IsFavoritesView,
                PageIndex = nextPage,
                PageSize = PageSize
            };

            var page = await _repository.QueryScoresAsync(query, ct);
            if (gen != _queryGeneration) return; // Stale query check

            foreach (var item in page.Items)
            {
                DisplayedScores.Add(new ScoreItemViewModel(item));
            }

            CurrentPageIndex = nextPage;
            TotalItemCount = page.TotalCount;
            HasMoreItems = DisplayedScores.Count < TotalItemCount;
            UpdateStatusText();
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    private void UpdateStatusText()
    {
        string baseCount = $"{TotalItemCount}개 항목";
        if (SelectedScore != null)
        {
            StatusText = $"{baseCount}     1개 선택됨";
        }
        else
        {
            StatusText = baseCount;
        }
    }

    private void UpdateEmptyState()
    {
        IsEmpty = DisplayedScores.Count == 0 && !IsLoading;
    }

    [RelayCommand]
    private async Task NavigateToFolderAsync(string? folderId)
    {
        if (CurrentFolderId != folderId || IsFavoritesView)
        {
            _backStack.Push(CurrentFolderId);
            _forwardStack.Clear();
            CanGoBack = _backStack.Count > 0;
            CanGoForward = false;

            IsFavoritesView = false;
            CurrentFolderId = folderId;
            await UpdateBreadcrumbAsync();
            await ReloadQueryAsync();
        }
    }

    [RelayCommand]
    private async Task NavigateToFavoritesAsync()
    {
        _backStack.Push(CurrentFolderId);
        _forwardStack.Clear();
        CanGoBack = _backStack.Count > 0;
        CanGoForward = false;

        IsFavoritesView = true;
        BreadcrumbPath = "즐겨찾기";
        CurrentFolderName = "즐겨찾기";
        CanGoUp = true;

        await ReloadQueryAsync();
    }

    [RelayCommand]
    private async Task NavigateBackAsync()
    {
        if (_backStack.Count > 0)
        {
            var prev = _backStack.Pop();
            _forwardStack.Push(CurrentFolderId);

            CanGoBack = _backStack.Count > 0;
            CanGoForward = _forwardStack.Count > 0;

            IsFavoritesView = false;
            CurrentFolderId = prev;
            await UpdateBreadcrumbAsync();
            await ReloadQueryAsync();
        }
    }

    [RelayCommand]
    private async Task NavigateForwardAsync()
    {
        if (_forwardStack.Count > 0)
            {
            var next = _forwardStack.Pop();
            _backStack.Push(CurrentFolderId);

            CanGoBack = _backStack.Count > 0;
            CanGoForward = _forwardStack.Count > 0;

            IsFavoritesView = false;
            CurrentFolderId = next;
            await UpdateBreadcrumbAsync();
            await ReloadQueryAsync();
        }
    }

    [RelayCommand]
    private async Task NavigateUpAsync()
    {
        if (IsFavoritesView)
        {
            await NavigateToFolderAsync(null);
            return;
        }

        if (!string.IsNullOrEmpty(CurrentFolderId))
        {
            var currentFolder = await _repository.GetFolderAsync(CurrentFolderId);
            await NavigateToFolderAsync(currentFolder?.ParentId);
        }
    }

    private async Task UpdateBreadcrumbAsync()
    {
        if (string.IsNullOrEmpty(CurrentFolderId))
        {
            BreadcrumbPath = "내 라이브러리";
            CurrentFolderName = "내 라이브러리";
            CanGoUp = false;
            return;
        }

        CanGoUp = true;
        var allFolders = (await _repository.GetAllFoldersAsync()).ToDictionary(f => f.Id);
        var parts = new List<string>();
        string? curId = CurrentFolderId;

        while (!string.IsNullOrEmpty(curId) && allFolders.TryGetValue(curId, out var f))
        {
            parts.Add(f.Name);
            curId = f.ParentId;
        }

        parts.Reverse();
        BreadcrumbPath = "내 라이브러리 > " + string.Join(" > ", parts);
        CurrentFolderName = parts.LastOrDefault() ?? "내 라이브러리";
    }

    [RelayCommand]
    private async Task CreateFolderAsync()
    {
        var newFolder = await _folderService.CreateFolderAsync("새 폴더", CurrentFolderId);
        FolderList.Add(new FolderItemViewModel(newFolder));
    }

    [RelayCommand]
    private async Task AddFileAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "지원 악보 (*.mid;*.midi;*.mml;*.txt)|*.mid;*.midi;*.mml;*.txt|모든 파일 (*.*)|*.*",
            Multiselect = true,
            Title = "악보 파일 추가"
        };

        if (dialog.ShowDialog() == true)
        {
            foreach (var file in dialog.FileNames)
            {
                try
                {
                    var score = await _libraryService.ImportExternalFileAsync(file, CurrentFolderId);
                    // Incremental addition to UI collection
                    DisplayedScores.Insert(0, new ScoreItemViewModel(score));
                    TotalItemCount++;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "가져오기 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            UpdateStatusText();
            UpdateEmptyState();
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedScoreAsync()
    {
        if (SelectedScore == null) return;

        var target = SelectedScore;
        await _libraryService.DeleteScoreAsync(target.Id);

        // Incremental removal
        DisplayedScores.Remove(target);
        TotalItemCount = Math.Max(0, TotalItemCount - 1);
        SelectedScore = null;
        UpdateStatusText();
        UpdateEmptyState();
    }

    [RelayCommand]
    private async Task RenameSelectedScoreAsync()
    {
        if (SelectedScore == null) return;

        var target = SelectedScore;
        string newTitle = target.Title + " (수정)";
        var updated = await _libraryService.RenameScoreAsync(target.Id, newTitle);

        // Incremental update
        target.UpdateFromModel(updated);
    }

    [RelayCommand]
    private async Task ToggleFavoriteScoreAsync(ScoreItemViewModel? scoreVm)
    {
        var target = scoreVm ?? SelectedScore;
        if (target == null) return;

        await _repository.ToggleFavoriteAsync(target.Id);
        target.Favorite = !target.Favorite;

        if (IsFavoritesView && !target.Favorite)
        {
            DisplayedScores.Remove(target);
            TotalItemCount = Math.Max(0, TotalItemCount - 1);
            UpdateStatusText();
            UpdateEmptyState();
        }
    }

    [RelayCommand]
    private void CutSelectedScore()
    {
        if (SelectedScore == null) return;
        _clipboardScore = SelectedScore;
        _isCut = true;
    }

    [RelayCommand]
    private void CopySelectedScore()
    {
        if (SelectedScore == null) return;
        _clipboardScore = SelectedScore;
        _isCut = false;
    }

    [RelayCommand]
    private async Task PasteScoreAsync()
    {
        if (_clipboardScore == null) return;

        if (_isCut)
        {
            await _libraryService.MoveScoreAsync(_clipboardScore.Id, CurrentFolderId);
            if (_clipboardScore.FolderId != CurrentFolderId)
            {
                DisplayedScores.Remove(_clipboardScore);
            }
            _clipboardScore = null;
        }
        else
        {
            var newScore = await _libraryService.CopyScoreAsync(_clipboardScore.Id, CurrentFolderId);
            DisplayedScores.Insert(0, new ScoreItemViewModel(newScore));
            TotalItemCount++;
        }

        UpdateStatusText();
        UpdateEmptyState();
    }
}
