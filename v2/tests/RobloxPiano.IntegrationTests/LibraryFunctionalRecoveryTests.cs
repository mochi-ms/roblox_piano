using System.Windows;
using RobloxPiano.Core.Library;
using RobloxPiano.Core.Services;
using RobloxPiano.Desktop.Services;
using RobloxPiano.Desktop.ViewModels;
using RobloxPiano.Infrastructure.Data;

namespace RobloxPiano.IntegrationTests;

public class FakeUserInteractionService : IUserInteractionService
{
    public string? NextPromptResponse { get; set; }
    public bool NextConfirmResponse { get; set; } = true;
    public List<string> ErrorMessages { get; } = new();
    public List<string> InfoMessages { get; } = new();
    public int PromptCount { get; private set; }
    public int ConfirmCount { get; private set; }

    public string? PromptText(string title, string message, string defaultText = "")
    {
        PromptCount++;
        return NextPromptResponse ?? defaultText;
    }

    public bool Confirm(string title, string message)
    {
        ConfirmCount++;
        return NextConfirmResponse;
    }

    public void ShowError(string title, string message)
    {
        ErrorMessages.Add($"{title}: {message}");
    }

    public void ShowInfo(string title, string message)
    {
        InfoMessages.Add($"{title}: {message}");
    }
}

public class LibraryFunctionalRecoveryTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _dbPath;
    private readonly string _storageRoot;
    private readonly SqliteLibraryRepository _repository;
    private readonly LibraryFileService _fileService;
    private readonly FolderService _folderService;
    private readonly LibraryService _libraryService;
    private readonly FakeUserInteractionService _interactionService;

    public LibraryFunctionalRecoveryTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"RP_RecoveryTest_{Guid.NewGuid():N}");
        _dbPath = Path.Combine(_testDir, "library.db");
        _storageRoot = Path.Combine(_testDir, "storage");
        Directory.CreateDirectory(_testDir);
        Directory.CreateDirectory(_storageRoot);

        _repository = new SqliteLibraryRepository(_dbPath);
        _fileService = new LibraryFileService(_storageRoot);
        _folderService = new FolderService(_repository, _fileService);
        _libraryService = new LibraryService(_repository, _fileService, _folderService);
        _interactionService = new FakeUserInteractionService();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch { }
    }

    private async Task<LibraryViewModel> CreateViewModelAsync()
    {
        var vm = new LibraryViewModel(_repository, _fileService, _folderService, _libraryService, _interactionService);
        await vm.InitializeAsync();
        return vm;
    }

    [Fact]
    public async Task CreateFolder_WithPrompt_CreatesHierarchicalFolder()
    {
        var vm = await CreateViewModelAsync();
        _interactionService.NextPromptResponse = "Classical Music";

        await vm.CreateFolderCommand.ExecuteAsync(null);

        Assert.Equal(1, _interactionService.PromptCount);
        Assert.Contains(vm.FolderList, f => f.Name == "Classical Music" && f.Depth == 0);

        // Now navigate into Classical Music and create a subfolder
        var parent = vm.FolderList.First(f => f.Name == "Classical Music");
        await vm.NavigateToFolderCommand.ExecuteAsync(parent.Id);

        _interactionService.NextPromptResponse = "Chopin";
        await vm.CreateFolderCommand.ExecuteAsync(null);

        Assert.Contains(vm.FolderList, f => f.Name == "Chopin" && f.Depth == 1 && f.ParentId == parent.Id);
    }

    [Fact]
    public async Task RenameSelectedScore_WithPrompt_UpdatesScoreTitleWithoutSuffix()
    {
        var vm = await CreateViewModelAsync();

        // Create sample file and import
        var samplePath = Path.Combine(_testDir, "test_score.mid");
        File.WriteAllBytes(samplePath, new byte[] { 0x4D, 0x54, 0x68, 0x64, 0x00, 0x00, 0x00, 0x06, 0x00, 0x00, 0x00, 0x01, 0x01, 0xE0 });
        var score = await _libraryService.ImportExternalFileAsync(samplePath, null);
        await vm.ReloadQueryAsync();

        Assert.NotEmpty(vm.DisplayedScores);
        var scoreVm = vm.DisplayedScores.First();
        vm.SelectedScore = scoreVm;

        _interactionService.NextPromptResponse = "Nocturne Op 9 No 2";
        await vm.RenameSelectedItemCommand.ExecuteAsync(null);

        Assert.Equal("Nocturne Op 9 No 2", scoreVm.Title);
        Assert.DoesNotContain("(수정)", scoreVm.Title);

        var fromDb = await _repository.GetScoreAsync(score.Id);
        Assert.NotNull(fromDb);
        Assert.Equal("Nocturne Op 9 No 2", fromDb.Title);
    }

    [Fact]
    public async Task RenameSelectedFolder_WithPrompt_UpdatesFolderNameAndBreadcrumb()
    {
        var vm = await CreateViewModelAsync();
        var folder = await _folderService.CreateFolderAsync("Original Folder", null);
        await vm.LoadFoldersAsync();

        var folderVm = vm.FolderList.First(f => f.Id == folder.Id);
        vm.SelectedFolder = folderVm;

        _interactionService.NextPromptResponse = "Renamed Folder";
        await vm.RenameSelectedItemCommand.ExecuteAsync(null);

        Assert.Equal("Renamed Folder", folderVm.Name);
        var fromDb = await _repository.GetFolderAsync(folder.Id);
        Assert.NotNull(fromDb);
        Assert.Equal("Renamed Folder", fromDb.Name);
    }

    [Fact]
    public async Task DeleteSelectedScore_WithConfirmation_RemovesScore()
    {
        var vm = await CreateViewModelAsync();
        var samplePath = Path.Combine(_testDir, "del_score.mid");
        File.WriteAllBytes(samplePath, new byte[] { 0x4D, 0x54, 0x68, 0x64, 0x00, 0x00, 0x00, 0x06, 0x00, 0x00, 0x00, 0x01, 0x01, 0xE0 });
        var score = await _libraryService.ImportExternalFileAsync(samplePath, null);
        await vm.ReloadQueryAsync();

        var target = vm.DisplayedScores.First();
        vm.SelectedScore = target;

        _interactionService.NextConfirmResponse = true;
        await vm.DeleteSelectedItemCommand.ExecuteAsync(null);

        Assert.Equal(1, _interactionService.ConfirmCount);
        Assert.DoesNotContain(target, vm.DisplayedScores);
        Assert.Equal(0, vm.TotalItemCount);
        Assert.Null(await _repository.GetScoreAsync(score.Id));
    }

    [Fact]
    public async Task DeleteSelectedScore_CancelConfirmation_PreservesScore()
    {
        var vm = await CreateViewModelAsync();
        var samplePath = Path.Combine(_testDir, "keep_score.mid");
        File.WriteAllBytes(samplePath, new byte[] { 0x4D, 0x54, 0x68, 0x64, 0x00, 0x00, 0x00, 0x06, 0x00, 0x00, 0x00, 0x01, 0x01, 0xE0 });
        var score = await _libraryService.ImportExternalFileAsync(samplePath, null);
        await vm.ReloadQueryAsync();

        var target = vm.DisplayedScores.First();
        vm.SelectedScore = target;

        _interactionService.NextConfirmResponse = false; // Cancel
        await vm.DeleteSelectedItemCommand.ExecuteAsync(null);

        Assert.Equal(1, _interactionService.ConfirmCount);
        Assert.Contains(target, vm.DisplayedScores);
        Assert.NotNull(await _repository.GetScoreAsync(score.Id));
    }

    [Fact]
    public async Task DeleteActiveFolder_NavigatesUpSafely()
    {
        var vm = await CreateViewModelAsync();
        var parentFolder = await _folderService.CreateFolderAsync("Parent", null);
        var subFolder = await _folderService.CreateFolderAsync("Sub", parentFolder.Id);
        await vm.NavigateToFolderCommand.ExecuteAsync(subFolder.Id);

        Assert.Equal(subFolder.Id, vm.CurrentFolderId);
        var subFolderVm = vm.FolderList.FirstOrDefault(f => f.Id == subFolder.Id);
        vm.SelectedFolder = subFolderVm;

        _interactionService.NextConfirmResponse = true;
        await vm.DeleteSelectedItemCommand.ExecuteAsync(null);

        // Should have navigated up to parent
        Assert.Equal(parentFolder.Id, vm.CurrentFolderId);
        Assert.Null(await _repository.GetFolderAsync(subFolder.Id));
    }

    [Fact]
    public async Task CutAndPaste_MovesScoreToNewFolder()
    {
        var vm = await CreateViewModelAsync();
        var destFolder = await _folderService.CreateFolderAsync("Destination", null);

        var samplePath = Path.Combine(_testDir, "cut_score.mid");
        File.WriteAllBytes(samplePath, new byte[] { 0x4D, 0x54, 0x68, 0x64, 0x00, 0x00, 0x00, 0x06, 0x00, 0x00, 0x00, 0x01, 0x01, 0xE0 });
        var score = await _libraryService.ImportExternalFileAsync(samplePath, null);
        await vm.ReloadQueryAsync();

        vm.SelectedScore = vm.DisplayedScores.First();
        vm.CutSelectedScoreCommand.Execute(null);
        Assert.True(vm.CanPaste);

        // Navigate to dest folder and paste
        await vm.NavigateToFolderCommand.ExecuteAsync(destFolder.Id);
        await vm.PasteScoreCommand.ExecuteAsync(null);

        var moved = await _repository.GetScoreAsync(score.Id);
        Assert.NotNull(moved);
        Assert.Equal(destFolder.Id, moved.FolderId);
        Assert.False(vm.CanPaste);
    }

    [Fact]
    public async Task CopyAndPaste_DuplicatesScoreInTargetFolder()
    {
        var vm = await CreateViewModelAsync();
        var samplePath = Path.Combine(_testDir, "copy_score.mid");
        File.WriteAllBytes(samplePath, new byte[] { 0x4D, 0x54, 0x68, 0x64, 0x00, 0x00, 0x00, 0x06, 0x00, 0x00, 0x00, 0x01, 0x01, 0xE0 });
        var score = await _libraryService.ImportExternalFileAsync(samplePath, null);
        await vm.ReloadQueryAsync();

        vm.SelectedScore = vm.DisplayedScores.First();
        vm.CopySelectedScoreCommand.Execute(null);
        Assert.True(vm.CanPaste);

        await vm.PasteScoreCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.TotalItemCount);
        Assert.Equal(2, vm.DisplayedScores.Count);
    }

    [Fact]
    public async Task OpenSelectedScore_FiresOpenScoreRequestedEvent()
    {
        var vm = await CreateViewModelAsync();
        var samplePath = Path.Combine(_testDir, "open_score.mid");
        File.WriteAllBytes(samplePath, new byte[] { 0x4D, 0x54, 0x68, 0x64, 0x00, 0x00, 0x00, 0x06, 0x00, 0x00, 0x00, 0x01, 0x01, 0xE0 });
        var score = await _libraryService.ImportExternalFileAsync(samplePath, null);
        await vm.ReloadQueryAsync();

        vm.SelectedScore = vm.DisplayedScores.First();

        ScoreItem? openedScore = null;
        vm.OpenScoreRequested += (_, s) => openedScore = s;

        vm.OpenSelectedScoreCommand.Execute(null);

        Assert.NotNull(openedScore);
        Assert.Equal(score.Id, openedScore.Id);
    }

    [Fact]
    public async Task MainViewModel_DoubleClicksScore_LoadsScoreInPlayerAndSwitchesView()
    {
        using var mainVm = new MainViewModel();
        var libVm = mainVm.LibraryViewModel;

        var samplePath = Path.Combine(_testDir, "main_open_score.mid");
        File.WriteAllBytes(samplePath, new byte[] { 0x4D, 0x54, 0x68, 0x64, 0x00, 0x00, 0x00, 0x06, 0x00, 0x00, 0x00, 0x01, 0x01, 0xE0 });
        var dummyScore = new ScoreItem
        {
            Id = "dummy-1",
            Title = "Main Test Score",
            FilePath = samplePath,
            SourceType = "MIDI",
            FileExtension = ".mid"
        };

        var scoreVm = new ScoreItemViewModel(dummyScore);
        libVm.DisplayedScores.Add(scoreVm);
        libVm.SelectedScore = scoreVm;

        // Switch to Library View first
        mainVm.NavigateCommand.Execute("라이브러리");
        Assert.Same(libVm, mainVm.CurrentView);

        // Open selected score
        libVm.OpenSelectedScoreCommand.Execute(null);

        // Must switch to PlayerViewModel
        Assert.Same(mainVm.PlayerViewModel, mainVm.CurrentView);
        Assert.Equal("Main Test Score", mainVm.PlayerViewModel.Title);
    }
}
