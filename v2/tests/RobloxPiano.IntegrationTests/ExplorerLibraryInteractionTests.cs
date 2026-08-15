using RobloxPiano.Core.Library;
using RobloxPiano.Core.Services;
using RobloxPiano.Desktop.Services;
using RobloxPiano.Desktop.ViewModels;
using RobloxPiano.Infrastructure.Data;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class ExplorerLibraryInteractionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly string _storageRoot;
    private readonly SqliteLibraryRepository _repository;
    private readonly LibraryFileService _fileService;
    private readonly FolderService _folderService;
    private readonly LibraryService _libraryService;
    private readonly FakeUserInteractionService _interaction;
    private readonly LibraryViewModel _viewModel;

    public ExplorerLibraryInteractionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"RP_EXPLORER_TEST_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test_library.db");
        _storageRoot = Path.Combine(_tempDir, "storage");

        _repository = new SqliteLibraryRepository(_dbPath);
        _fileService = new LibraryFileService(_storageRoot);
        _folderService = new FolderService(_repository, _fileService);
        _libraryService = new LibraryService(_repository, _fileService, _folderService);
        _interaction = new FakeUserInteractionService();

        _viewModel = new LibraryViewModel(_repository, _fileService, _folderService, _libraryService, _interaction);
    }

    private class FakeUserInteractionService : IUserInteractionService
    {
        public bool ConfirmResponse { get; set; } = true;
        public string? PromptResponse { get; set; } = "Test Prompt Input";
        public List<string> ErrorsShown { get; } = new();
        public List<string> InfoShown { get; } = new();

        public bool Confirm(string title, string message) => ConfirmResponse;
        public string? PromptText(string title, string message, string defaultValue = "") => PromptResponse;
        public void ShowError(string title, string message) => ErrorsShown.Add($"{title}: {message}");
        public void ShowInfo(string title, string message) => InfoShown.Add($"{title}: {message}");
    }

    [Fact]
    public async Task MultiSelect_SelectionUpdate_ReflectsInStatusTextAndCommands()
    {
        await _viewModel.InitializeAsync();

        // Create 3 scores
        var score1 = await _libraryService.CreateScoreFromTextAsync("MML@T120C;", "Score 1", null);
        var score2 = await _libraryService.CreateScoreFromTextAsync("MML@T120D;", "Score 2", null);
        var score3 = await _libraryService.CreateScoreFromTextAsync("MML@T120E;", "Score 3", null);
        await _viewModel.ReloadQueryAsync();

        Assert.Equal(3, _viewModel.DisplayedScores.Count);

        // Select 2 scores
        var sel = new[] { _viewModel.DisplayedScores[0], _viewModel.DisplayedScores[1] };
        _viewModel.UpdateSelectedScores(sel);

        Assert.Equal(2, _viewModel.SelectedScores.Count);
        Assert.Contains("2개 선택됨", _viewModel.StatusText);
        Assert.True(_viewModel.CanCopySelectedItem);
        Assert.True(_viewModel.CanCutSelectedItem);
        Assert.True(_viewModel.CanDeleteSelectedItem);
        Assert.False(_viewModel.CanRenameSelectedItem); // Multi-rename is disabled in standard explorer

        // Single select
        _viewModel.UpdateSelectedScores(new[] { _viewModel.DisplayedScores[0] });
        Assert.Contains("1개 선택됨", _viewModel.StatusText);
        Assert.True(_viewModel.CanRenameSelectedItem);
    }

    [Fact]
    public async Task BatchDelete_DeletesMultipleSelectedScores()
    {
        await _viewModel.InitializeAsync();

        var score1 = await _libraryService.CreateScoreFromTextAsync("MML@T120C;", "Score 1", null);
        var score2 = await _libraryService.CreateScoreFromTextAsync("MML@T120D;", "Score 2", null);
        var score3 = await _libraryService.CreateScoreFromTextAsync("MML@T120E;", "Score 3", null);
        await _viewModel.ReloadQueryAsync();

        _viewModel.UpdateSelectedScores(new[] { _viewModel.DisplayedScores[0], _viewModel.DisplayedScores[1] });
        await _viewModel.DeleteSelectedItemCommand.ExecuteAsync(null);

        Assert.Single(_viewModel.DisplayedScores);
        Assert.Equal("Score 3", _viewModel.DisplayedScores[0].Title);
        Assert.Empty(_viewModel.SelectedScores);
    }

    [Fact]
    public async Task BatchCopyAndPaste_DuplicatesSelectedScores()
    {
        await _viewModel.InitializeAsync();

        var score1 = await _libraryService.CreateScoreFromTextAsync("MML@T120C;", "Score 1", null);
        var score2 = await _libraryService.CreateScoreFromTextAsync("MML@T120D;", "Score 2", null);
        await _viewModel.ReloadQueryAsync();

        _viewModel.UpdateSelectedScores(new[] { _viewModel.DisplayedScores[0], _viewModel.DisplayedScores[1] });
        _viewModel.CopySelectedScore();
        Assert.True(_viewModel.CanPaste);

        await _viewModel.PasteScoreCommand.ExecuteAsync(null);

        Assert.Equal(4, _viewModel.DisplayedScores.Count);
    }

    [Fact]
    public async Task MoveScoresToFolder_MovesSelectedScoresCorrectly()
    {
        await _viewModel.InitializeAsync();

        var folder = await _folderService.CreateFolderAsync("Sub Folder", null);
        var score1 = await _libraryService.CreateScoreFromTextAsync("MML@T120C;", "Score 1", null);
        var score2 = await _libraryService.CreateScoreFromTextAsync("MML@T120D;", "Score 2", null);
        await _viewModel.ReloadQueryAsync();

        Assert.Equal(2, _viewModel.DisplayedScores.Count);

        // Move score 1 to Sub Folder
        await _viewModel.MoveScoresToFolderAsync(new[] { score1.Id }, folder.Id);

        // Current root view should have only score 2 left
        Assert.Single(_viewModel.DisplayedScores);
        Assert.Equal(score2.Id, _viewModel.DisplayedScores[0].Id);

        // Navigate to Sub Folder
        await _viewModel.NavigateToFolderCommand.ExecuteAsync(folder.Id);
        Assert.Single(_viewModel.DisplayedScores);
        Assert.Equal(score1.Id, _viewModel.DisplayedScores[0].Id);
    }

    [Fact]
    public async Task BreadcrumbNavigation_GeneratesClickableSegments()
    {
        await _viewModel.InitializeAsync();

        var parent = await _folderService.CreateFolderAsync("Parent Folder", null);
        var child = await _folderService.CreateFolderAsync("Child Folder", parent.Id);

        await _viewModel.NavigateToFolderCommand.ExecuteAsync(child.Id);

        Assert.Equal(3, _viewModel.BreadcrumbSegments.Count);
        Assert.Equal("내 라이브러리", _viewModel.BreadcrumbSegments[0].Name);
        Assert.Null(_viewModel.BreadcrumbSegments[0].FolderId);
        Assert.False(_viewModel.BreadcrumbSegments[0].IsLast);

        Assert.Equal("Parent Folder", _viewModel.BreadcrumbSegments[1].Name);
        Assert.Equal(parent.Id, _viewModel.BreadcrumbSegments[1].FolderId);
        Assert.False(_viewModel.BreadcrumbSegments[1].IsLast);

        Assert.Equal("Child Folder", _viewModel.BreadcrumbSegments[2].Name);
        Assert.Equal(child.Id, _viewModel.BreadcrumbSegments[2].FolderId);
        Assert.True(_viewModel.BreadcrumbSegments[2].IsLast);
    }

    [Fact]
    public async Task ExternalFileDrop_ImportsDroppedFiles()
    {
        await _viewModel.InitializeAsync();

        string sampleMml = Path.Combine(_tempDir, "sample_drop.mml");
        File.WriteAllText(sampleMml, "MML@T120CDEF,GAB>C;");

        await _viewModel.ImportFilesAsync(new[] { sampleMml });

        Assert.Single(_viewModel.DisplayedScores);
        Assert.Equal("sample_drop", _viewModel.DisplayedScores[0].Title);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }
}
