using System.Collections.ObjectModel;
using System.IO;
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

    private readonly List<ScoreItemViewModel> _clipboardScores = new();
    private bool _isCut;

    [ObservableProperty]
    private ObservableCollection<ScoreItemViewModel> _displayedScores = new();

    [ObservableProperty]
    private ObservableCollection<FolderItemViewModel> _folderList = new();

    [ObservableProperty]
    private ObservableCollection<BreadcrumbItemViewModel> _breadcrumbSegments = new();

    [ObservableProperty]
    private ObservableCollection<ScoreItemViewModel> _selectedScores = new();

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

    public bool HasSelection => SelectedScores.Count > 0 || SelectedScore != null || (SelectedFolder != null && !IsFavoritesView);
    public bool HasScoreSelected => SelectedScores.Count > 0 || SelectedScore != null;
    public bool CanRenameSelectedItem => (SelectedScores.Count == 1 || (SelectedScores.Count == 0 && SelectedScore != null)) || (SelectedFolder != null && !IsFavoritesView && SelectedScores.Count == 0);
    public bool CanDeleteSelectedItem => SelectedScores.Count > 0 || SelectedScore != null || (SelectedFolder != null && !IsFavoritesView);
    public bool CanCutSelectedItem => SelectedScores.Count > 0 || SelectedScore != null;
    public bool CanCopySelectedItem => SelectedScores.Count > 0 || SelectedScore != null;
    public bool HasSearchText => !string.IsNullOrEmpty(SearchText);

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
            await UpdateBreadcrumbAsync();
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
        OnPropertyChanged(nameof(HasSearchText));
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

    [RelayCommand]
    public void ClearSearch()
    {
        SearchText = string.Empty;
    }

    partial void OnSelectedScoreChanged(ScoreItemViewModel? value)
    {
        if (value != null && !SelectedScores.Contains(value))
        {
            SelectedScores.Clear();
            SelectedScores.Add(value);
        }
        else if (value == null && SelectedScores.Count <= 1)
        {
            SelectedScores.Clear();
        }
        UpdateStatusText();
        UpdateSelectionCommandStates();
    }

    partial void OnSelectedFolderChanged(FolderItemViewModel? value)
    {
        UpdateSelectionCommandStates();
    }

    public void UpdateSelectedScores(IEnumerable<ScoreItemViewModel> items)
    {
        SelectedScores.Clear();
        foreach (var item in items)
        {
            SelectedScores.Add(item);
        }
        SelectedScore = SelectedScores.FirstOrDefault();
        UpdateStatusText();
        UpdateSelectionCommandStates();
    }

    public void ClearSelection()
    {
        SelectedScores.Clear();
        SelectedScore = null;
        SelectedFolder = null;
        UpdateStatusText();
        UpdateSelectionCommandStates();
    }

    private void UpdateSelectionCommandStates()
    {
        OnPropertyChanged(nameof(HasSelection));
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
            SelectedScores.Clear();
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
    public async Task RefreshLibraryAsync()
    {
        await LoadFoldersAsync();
        await UpdateBreadcrumbAsync();
        await ReloadQueryAsync();
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
            if (CurrentSortColumn == col)
            {
                SortDescending = !SortDescending;
            }
            else
            {
                CurrentSortColumn = col;
                SortDescending = false;
            }
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
        int selCount = SelectedScores.Count > 0 ? SelectedScores.Count : (SelectedScore != null ? 1 : 0);
        if (selCount > 0)
        {
            StatusText = $"{baseCount}     {selCount}개 선택됨";
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

        BreadcrumbSegments.Clear();
        BreadcrumbSegments.Add(new BreadcrumbItemViewModel("내 라이브러리", null));
        BreadcrumbSegments.Add(new BreadcrumbItemViewModel("즐겨찾기", null, isLast: true));

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
        BreadcrumbSegments.Clear();

        if (string.IsNullOrEmpty(CurrentFolderId))
        {
            BreadcrumbPath = "내 라이브러리";
            CurrentFolderName = "내 라이브러리";
            CanGoUp = false;
            BreadcrumbSegments.Add(new BreadcrumbItemViewModel("내 라이브러리", null, isLast: true));
            return;
        }

        CanGoUp = true;
        var allFolders = (await _repository.GetAllFoldersAsync()).ToDictionary(f => f.Id);
        var chain = new List<(string Name, string Id)>();
        string? curId = CurrentFolderId;

        while (!string.IsNullOrEmpty(curId) && allFolders.TryGetValue(curId, out var f))
        {
            chain.Add((f.Name, f.Id));
            curId = f.ParentId;
        }

        chain.Reverse();
        BreadcrumbPath = "내 라이브러리 > " + string.Join(" > ", chain.Select(c => c.Name));
        CurrentFolderName = chain.LastOrDefault().Name ?? "내 라이브러리";

        BreadcrumbSegments.Add(new BreadcrumbItemViewModel("내 라이브러리", null, isLast: chain.Count == 0));
        for (int i = 0; i < chain.Count; i++)
        {
            bool isLast = (i == chain.Count - 1);
            BreadcrumbSegments.Add(new BreadcrumbItemViewModel(chain[i].Name, chain[i].Id, isLast));
        }
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
    public async Task CreateSubFolderAsync(string? parentFolderId)
    {
        string? targetParent = parentFolderId ?? SelectedFolder?.Id ?? CurrentFolderId;
        var folderName = _interactionService.PromptText("새 하위 폴더", "하위 폴더 이름을 입력하세요:", "새 하위 폴더");
        if (string.IsNullOrWhiteSpace(folderName)) return;

        folderName = folderName.Trim();
        try
        {
            await _folderService.CreateFolderAsync(folderName, targetParent);
            await LoadFoldersAsync();
        }
        catch (Exception ex)
        {
            _interactionService.ShowError("하위 폴더 생성 실패", ex.Message);
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
            await ImportFilesAsync(dialog.FileNames, CurrentFolderId);
        }
    }

    public async Task ImportFilesAsync(IEnumerable<string> filePaths, string? targetFolderId = null)
    {
        int importedCount = 0;
        foreach (var file in filePaths)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) continue;

            try
            {
                var score = await _libraryService.ImportExternalFileAsync(file, targetFolderId ?? CurrentFolderId);
                if (targetFolderId == null || targetFolderId == CurrentFolderId)
                {
                    DisplayedScores.Insert(0, new ScoreItemViewModel(score));
                    TotalItemCount++;
                }
                importedCount++;
            }
            catch (Exception ex)
            {
                _interactionService.ShowError("가져오기 실패", $"{Path.GetFileName(file)}: {ex.Message}");
            }
        }

        if (importedCount > 0)
        {
            UpdateStatusText();
            UpdateEmptyState();
        }
    }

    public async Task MoveScoresToFolderAsync(IEnumerable<string> scoreIds, string? targetFolderId)
    {
        if (IsFavoritesView) return;

        int movedCount = 0;
        foreach (var id in scoreIds)
        {
            try
            {
                await _libraryService.MoveScoreAsync(id, targetFolderId);
                var existing = DisplayedScores.FirstOrDefault(s => s.Id == id);
                if (existing != null && targetFolderId != CurrentFolderId)
                {
                    DisplayedScores.Remove(existing);
                    TotalItemCount = Math.Max(0, TotalItemCount - 1);
                }
                movedCount++;
            }
            catch (Exception ex)
            {
                _interactionService.ShowError("이동 실패", ex.Message);
            }
        }

        if (movedCount > 0)
        {
            UpdateStatusText();
            UpdateEmptyState();
            UpdateSelectionCommandStates();
        }
    }

    [RelayCommand]
    public async Task DeleteSelectedItemAsync()
    {
        var targets = SelectedScores.ToList();
        if (targets.Count == 0 && SelectedScore != null)
        {
            targets.Add(SelectedScore);
        }

        if (targets.Count > 0)
        {
            string confirmMsg = targets.Count == 1
                ? $"'{targets[0].Title}' 악보를 삭제하시겠습니까?"
                : $"선택한 {targets.Count}개의 악보를 삭제하시겠습니까?";

            if (!_interactionService.Confirm("악보 삭제", confirmMsg))
                return;

            int deletedCount = 0;
            foreach (var target in targets)
            {
                try
                {
                    await _libraryService.DeleteScoreAsync(target.Id);
                    DisplayedScores.Remove(target);
                    deletedCount++;
                }
                catch (Exception ex)
                {
                    _interactionService.ShowError("삭제 실패", $"{target.Title}: {ex.Message}");
                }
            }

            TotalItemCount = Math.Max(0, TotalItemCount - deletedCount);
            SelectedScores.Clear();
            SelectedScore = null;
            UpdateStatusText();
            UpdateEmptyState();
            UpdateSelectionCommandStates();
        }
        else if (SelectedFolder != null && !IsFavoritesView)
        {
            var target = SelectedFolder;
            if (!_interactionService.Confirm("폴더 삭제", $"'{target.Name}' 폴더와 안의 악보를 모두 삭제하시겠습니까?"))
                return;

            try
            {
                await _folderService.DeleteFolderAsync(target.Id);
                
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
        var target = SelectedScores.Count == 1 ? SelectedScores[0] : SelectedScore;
        if (target != null)
        {
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
        else if (SelectedFolder != null && !IsFavoritesView && SelectedScores.Count == 0)
        {
            var folderTarget = SelectedFolder;
            var currentName = folderTarget.Name;
            var newName = _interactionService.PromptText("폴더 이름 변경", "새 폴더 이름을 입력하세요:", currentName);
            if (string.IsNullOrWhiteSpace(newName) || newName.Trim() == currentName) return;

            newName = newName.Trim();
            try
            {
                var updated = await _folderService.RenameFolderAsync(folderTarget.Id, newName);
                folderTarget.Name = updated.Name;
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
        var targets = scoreVm != null ? new List<ScoreItemViewModel> { scoreVm } : SelectedScores.ToList();
        if (targets.Count == 0 && SelectedScore != null)
        {
            targets.Add(SelectedScore);
        }
        if (targets.Count == 0) return;

        foreach (var target in targets)
        {
            await _repository.ToggleFavoriteAsync(target.Id);
            target.Favorite = !target.Favorite;

            if (IsFavoritesView && !target.Favorite)
            {
                DisplayedScores.Remove(target);
                TotalItemCount = Math.Max(0, TotalItemCount - 1);
            }
        }

        if (IsFavoritesView)
        {
            UpdateStatusText();
            UpdateEmptyState();
        }
    }

    [RelayCommand]
    public void CutSelectedScore()
    {
        var targets = SelectedScores.Count > 0 ? SelectedScores.ToList() : (SelectedScore != null ? new List<ScoreItemViewModel> { SelectedScore } : new List<ScoreItemViewModel>());
        if (targets.Count == 0) return;

        _clipboardScores.Clear();
        _clipboardScores.AddRange(targets);
        _isCut = true;
        CanPaste = true;
        UpdateSelectionCommandStates();
    }

    [RelayCommand]
    public void CopySelectedScore()
    {
        var targets = SelectedScores.Count > 0 ? SelectedScores.ToList() : (SelectedScore != null ? new List<ScoreItemViewModel> { SelectedScore } : new List<ScoreItemViewModel>());
        if (targets.Count == 0) return;

        _clipboardScores.Clear();
        _clipboardScores.AddRange(targets);
        _isCut = false;
        CanPaste = true;
        UpdateSelectionCommandStates();
    }

    [RelayCommand]
    public async Task PasteScoreAsync()
    {
        if (_clipboardScores.Count == 0 || IsFavoritesView) return;

        try
        {
            if (_isCut)
            {
                foreach (var item in _clipboardScores)
                {
                    await _libraryService.MoveScoreAsync(item.Id, CurrentFolderId);
                }
                _clipboardScores.Clear();
                CanPaste = false;
                await ReloadQueryAsync();
            }
            else
            {
                foreach (var item in _clipboardScores)
                {
                    var newScore = await _libraryService.CopyScoreAsync(item.Id, CurrentFolderId);
                    DisplayedScores.Insert(0, new ScoreItemViewModel(newScore));
                    TotalItemCount++;
                }
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
        var target = SelectedScores.Count == 1 ? SelectedScores[0] : SelectedScore;
        if (target != null)
        {
            OpenScoreRequested?.Invoke(this, target.Model);
        }
    }
}
