using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobloxPiano.Core.Library;
using RobloxPiano.Core.Services;
using RobloxPiano.Desktop.Services;
using RobloxPiano.Infrastructure.Data;

namespace RobloxPiano.Desktop.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly ILibraryRepository _repository;
    private readonly LibraryFileService _fileService;
    private readonly FolderService _folderService;
    private readonly LibraryService _libraryService;
    private readonly IUserInteractionService _interactionService;

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
    private FolderItemViewModel? _selectedFolder;

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
    private bool _isRootView = true;

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

    [ObservableProperty]
    private bool _canPaste = false;

    [ObservableProperty]
    private LibrarySortColumn _currentSortColumn = LibrarySortColumn.Title;

    [ObservableProperty]
    private bool _sortDescending = false;

    public bool IsSortByTitle => CurrentSortColumn == LibrarySortColumn.Title;
    public bool IsSortByType => CurrentSortColumn == LibrarySortColumn.FileExtension;
    public bool IsSortByDuration => CurrentSortColumn == LibrarySortColumn.Duration;
    public bool IsSortByBpm => CurrentSortColumn == LibrarySortColumn.Bpm;
    public bool IsSortByNotes => CurrentSortColumn == LibrarySortColumn.TotalNotes;
    public bool IsSortByModified => CurrentSortColumn == LibrarySortColumn.UpdatedAt;
    public bool IsSortAscending => !SortDescending;
    public bool IsSortDescending => SortDescending;

    public bool HasScoreSelected => SelectedScore != null;
    public bool CanRenameSelectedItem => (SelectedScore != null) || (SelectedFolder != null && !IsFavoritesView);
    public bool CanDeleteSelectedItem => (SelectedScore != null) || (SelectedFolder != null && !IsFavoritesView);
    public bool CanCutSelectedItem => SelectedScore != null;
    public bool CanCopySelectedItem => SelectedScore != null;

    public LibraryViewModel()
    {
        var dbPath = LibraryDatabasePathProvider.GetDefaultDatabasePath();
        var storageRoot = LibraryDatabasePathProvider.GetDefaultLibraryStorageRoot();

        _repository = new SqliteLibraryRepository(dbPath);
        _fileService = new LibraryFileService(storageRoot);
        _folderService = new FolderService(_repository, _fileService);
        _libraryService = new LibraryService(_repository, _fileService, _folderService);
        _interactionService = new WpfUserInteractionService();

        _ = InitializeAsync();
    }

    public LibraryViewModel(
        ILibraryRepository repository,
        LibraryFileService fileService,
        FolderService folderService,
        LibraryService libraryService,
        IUserInteractionService? interactionService = null)
    {
        _repository = repository;
        _fileService = fileService;
        _folderService = folderService;
        _libraryService = libraryService;
        _interactionService = interactionService ?? new WpfUserInteractionService();

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
            UpdateSelectionCommandStates();
        }
    }

    partial void OnCurrentSortColumnChanged(LibrarySortColumn value)
    {
        OnPropertyChanged(nameof(IsSortByTitle));
        OnPropertyChanged(nameof(IsSortByType));
        OnPropertyChanged(nameof(IsSortByDuration));
        OnPropertyChanged(nameof(IsSortByBpm));
        OnPropertyChanged(nameof(IsSortByNotes));
        OnPropertyChanged(nameof(IsSortByModified));
    }

    partial void OnSortDescendingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSortAscending));
        OnPropertyChanged(nameof(IsSortDescending));
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
                    if (Application.Current != null)
                    {
                        await Application.Current.Dispatcher.InvokeAsync(async () =>
                        {
                            await ReloadQueryAsync(ct);
                        });
                    }
                    else
                    {
                        await ReloadQueryAsync(ct);
                    }
                }
            }
            catch (TaskCanceledException) { }
        }, ct);
    }

    partial void OnSelectedScoreChanged(ScoreItemViewModel? value)
    {
        UpdateStatusText();
        UpdateSelectionCommandStates();
    }

    partial void OnSelectedFolderChanged(FolderItemViewModel? value)
    {
        UpdateSelectionCommandStates();
    }

    private void UpdateSelectionCommandStates()
    {
        OnPropertyChanged(nameof(HasScoreSelected));
        OnPropertyChanged(nameof(CanRenameSelectedItem));
        OnPropertyChanged(nameof(CanDeleteSelectedItem));
        OnPropertyChanged(nameof(CanCutSelectedItem));
        OnPropertyChanged(nameof(CanCopySelectedItem));
        OnPropertyChanged(nameof(CanPaste));
    }

    public async Task LoadFoldersAsync(CancellationToken ct = default)
    {
        var folders = await _repository.GetAllFoldersAsync(ct);
        
        // Group by parent to build deterministic hierarchy
        var byParent = folders
            .GroupBy(f => f.ParentId ?? string.Empty)
            .ToDictionary(g => g.Key, g => g.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList());

        var result = new List<FolderItemViewModel>();

        void AddSubtree(string parentIdKey, int depth)
        {
            if (byParent.TryGetValue(parentIdKey, out var children))
            {
                foreach (var folder in children)
                {
                    var vm = new FolderItemViewModel(folder, depth)
                    {
                        IsCurrent = folder.Id == CurrentFolderId
                    };
                    result.Add(vm);
                    AddSubtree(folder.Id, depth + 1);
                }
            }
        }

        AddSubtree(string.Empty, 0);

        FolderList.Clear();
        foreach (var item in result)
        {
            FolderList.Add(item);
        }

        // Update selected folder VM if current folder is set
        SelectedFolder = FolderList.FirstOrDefault(f => f.Id == CurrentFolderId);
        UpdateSelectionCommandStates();
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
                SortBy = CurrentSortColumn,
                SortDescending = SortDescending,
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
            SelectedScore = null;
            UpdateStatusText();
            UpdateEmptyState();
        }
        finally
        {
            IsLoading = false;
            UpdateSelectionCommandStates();
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
                SortBy = CurrentSortColumn,
                SortDescending = SortDescending,
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

    [RelayCommand]
    public async Task SetSortColumnAsync(string columnName)
    {
        if (Enum.TryParse<LibrarySortColumn>(columnName, true, out var col))
        {
            CurrentSortColumn = col;
            await ReloadQueryAsync();
        }
    }

    [RelayCommand]
    public async Task SetSortDirectionAsync(object? param)
    {
        bool desc = false;
        if (param is bool b)
        {
            desc = b;
        }
        else if (param is string s && bool.TryParse(s, out var parsed))
        {
            desc = parsed;
        }

        SortDescending = desc;
        await ReloadQueryAsync();
    }

    [RelayCommand]
    public async Task ToggleSortDirectionAsync()
    {
        SortDescending = !SortDescending;
        await ReloadQueryAsync();
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
    public async Task NavigateToFolderAsync(string? folderId)
    {
        if (CurrentFolderId != folderId || IsFavoritesView)
        {
            _backStack.Push(CurrentFolderId);
            _forwardStack.Clear();
            CanGoBack = _backStack.Count > 0;
            CanGoForward = false;

            IsFavoritesView = false;
            CurrentFolderId = folderId;
            IsRootView = string.IsNullOrEmpty(folderId);
            await UpdateBreadcrumbAsync();
            await LoadFoldersAsync();
            await ReloadQueryAsync();
        }
    }

    [RelayCommand]
    public async Task NavigateToFavoritesAsync()
    {
        _backStack.Push(CurrentFolderId);
        _forwardStack.Clear();
        CanGoBack = _backStack.Count > 0;
        CanGoForward = false;

        IsFavoritesView = true;
        IsRootView = false;
        BreadcrumbPath = "즐겨찾기";
        CurrentFolderName = "즐겨찾기";
        CanGoUp = true;
        SelectedFolder = null;

        await LoadFoldersAsync();
        await ReloadQueryAsync();
    }

    [RelayCommand]
    public async Task NavigateBackAsync()
    {
        if (_backStack.Count > 0)
        {
            var prev = _backStack.Pop();
            _forwardStack.Push(CurrentFolderId);

            CanGoBack = _backStack.Count > 0;
            CanGoForward = _forwardStack.Count > 0;

            IsFavoritesView = false;
            CurrentFolderId = prev;
            IsRootView = string.IsNullOrEmpty(prev);
            await UpdateBreadcrumbAsync();
            await LoadFoldersAsync();
            await ReloadQueryAsync();
        }
    }

    [RelayCommand]
    public async Task NavigateForwardAsync()
    {
        if (_forwardStack.Count > 0)
        {
            var next = _forwardStack.Pop();
            _backStack.Push(CurrentFolderId);

            CanGoBack = _backStack.Count > 0;
            CanGoForward = _forwardStack.Count > 0;

            IsFavoritesView = false;
            CurrentFolderId = next;
            IsRootView = string.IsNullOrEmpty(next);
            await UpdateBreadcrumbAsync();
            await LoadFoldersAsync();
            await ReloadQueryAsync();
        }
    }

    [RelayCommand]
    public async Task NavigateUpAsync()
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
    public async Task CreateFolderAsync()
    {
        var folderName = _interactionService.PromptText("새 폴더", "폴더 이름을 입력하세요:", "새 폴더");
        if (string.IsNullOrWhiteSpace(folderName)) return;

        folderName = folderName.Trim();
        try
        {
            var newFolder = await _folderService.CreateFolderAsync(folderName, CurrentFolderId);
            await LoadFoldersAsync();
        }
        catch (Exception ex)
        {
            _interactionService.ShowError("폴더 생성 실패", ex.Message);
        }
    }

    [RelayCommand]
    public async Task AddFilesAsync()
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
                    DisplayedScores.Insert(0, new ScoreItemViewModel(score));
                    TotalItemCount++;
                }
                catch (Exception ex)
                {
                    _interactionService.ShowError("가져오기 실패", ex.Message);
                }
            }
            UpdateStatusText();
            UpdateEmptyState();
        }
    }

    [RelayCommand]
    public async Task DeleteSelectedItemAsync()
    {
        if (SelectedScore != null)
        {
            var target = SelectedScore;
            if (!_interactionService.Confirm("악보 삭제", $"'{target.Title}' 악보를 삭제하시겠습니까?"))
                return;

            try
            {
                await _libraryService.DeleteScoreAsync(target.Id);
                DisplayedScores.Remove(target);
                TotalItemCount = Math.Max(0, TotalItemCount - 1);
                SelectedScore = null;
                UpdateStatusText();
                UpdateEmptyState();
                UpdateSelectionCommandStates();
            }
            catch (Exception ex)
            {
                _interactionService.ShowError("삭제 실패", ex.Message);
            }
        }
        else if (SelectedFolder != null && !IsFavoritesView)
        {
            var target = SelectedFolder;
            if (!_interactionService.Confirm("폴더 삭제", $"'{target.Name}' 폴더와 안의 악보를 모두 삭제하시겠습니까?"))
                return;

            try
            {
                await _folderService.DeleteFolderAsync(target.Id);
                
                // If deleted folder was active, navigate to parent
                if (CurrentFolderId == target.Id)
                {
                    await NavigateToFolderAsync(target.ParentId);
                }
                else
                {
                    await LoadFoldersAsync();
                    await ReloadQueryAsync();
                }
                SelectedFolder = null;
                UpdateSelectionCommandStates();
            }
            catch (Exception ex)
            {
                _interactionService.ShowError("폴더 삭제 실패", ex.Message);
            }
        }
    }

    [RelayCommand]
    public async Task RenameSelectedItemAsync()
    {
        if (SelectedScore != null)
        {
            var target = SelectedScore;
            var currentTitle = target.Title;
            var newTitle = _interactionService.PromptText("악보 이름 변경", "새 악보 이름을 입력하세요:", currentTitle);
            if (string.IsNullOrWhiteSpace(newTitle) || newTitle.Trim() == currentTitle) return;

            newTitle = newTitle.Trim();
            try
            {
                var updated = await _libraryService.RenameScoreAsync(target.Id, newTitle);
                target.UpdateFromModel(updated);
            }
            catch (Exception ex)
            {
                _interactionService.ShowError("이름 변경 실패", ex.Message);
            }
        }
        else if (SelectedFolder != null && !IsFavoritesView)
        {
            var target = SelectedFolder;
            var currentName = target.Name;
            var newName = _interactionService.PromptText("폴더 이름 변경", "새 폴더 이름을 입력하세요:", currentName);
            if (string.IsNullOrWhiteSpace(newName) || newName.Trim() == currentName) return;

            newName = newName.Trim();
            try
            {
                var updated = await _folderService.RenameFolderAsync(target.Id, newName);
                target.Name = updated.Name;
                await UpdateBreadcrumbAsync();
                await LoadFoldersAsync();
            }
            catch (Exception ex)
            {
                _interactionService.ShowError("이름 변경 실패", ex.Message);
            }
        }
    }

    [RelayCommand]
    public async Task ToggleFavoriteScoreAsync(ScoreItemViewModel? scoreVm)
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
    public void CutSelectedScore()
    {
        if (SelectedScore == null) return;
        _clipboardScore = SelectedScore;
        _isCut = true;
        CanPaste = true;
        UpdateSelectionCommandStates();
    }

    [RelayCommand]
    public void CopySelectedScore()
    {
        if (SelectedScore == null) return;
        _clipboardScore = SelectedScore;
        _isCut = false;
        CanPaste = true;
        UpdateSelectionCommandStates();
    }

    [RelayCommand]
    public async Task PasteScoreAsync()
    {
        if (_clipboardScore == null || IsFavoritesView) return;

        try
        {
            if (_isCut)
            {
                await _libraryService.MoveScoreAsync(_clipboardScore.Id, CurrentFolderId);
                _clipboardScore = null;
                CanPaste = false;
                await ReloadQueryAsync();
            }
            else
            {
                var newScore = await _libraryService.CopyScoreAsync(_clipboardScore.Id, CurrentFolderId);
                DisplayedScores.Insert(0, new ScoreItemViewModel(newScore));
                TotalItemCount++;
                UpdateStatusText();
                UpdateEmptyState();
            }
        }
        catch (Exception ex)
        {
            _interactionService.ShowError("붙여넣기 실패", ex.Message);
        }
        finally
        {
            UpdateSelectionCommandStates();
        }
    }

    public event EventHandler<ScoreItem>? OpenScoreRequested;

    [RelayCommand]
    public void OpenSelectedScore()
    {
        if (SelectedScore != null)
        {
            OpenScoreRequested?.Invoke(this, SelectedScore.Model);
        }
    }
}
