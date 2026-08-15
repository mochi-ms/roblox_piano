using RobloxPiano.Core.Library;
using RobloxPiano.Core.Services;
using RobloxPiano.Infrastructure.Data;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class FolderServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly string _storageRoot;
    private readonly SqliteLibraryRepository _repository;
    private readonly LibraryFileService _fileService;
    private readonly FolderService _folderService;

    public FolderServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"folder_svc_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _dbPath = Path.Combine(_tempDir, "library.db");
        _storageRoot = Path.Combine(_tempDir, "Storage");
        Directory.CreateDirectory(_storageRoot);

        _repository = new SqliteLibraryRepository(_dbPath);
        _fileService = new LibraryFileService(_storageRoot);
        _folderService = new FolderService(_repository, _fileService);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task CreateFolder_CollisionNumbering_WorksCorrectly()
    {
        await _repository.InitializeAsync();

        var f1 = await _folderService.CreateFolderAsync("Anime");
        Assert.Equal("Anime", f1.Name);

        var f2 = await _folderService.CreateFolderAsync("Anime");
        Assert.Equal("Anime (1)", f2.Name);

        var f3 = await _folderService.CreateFolderAsync("Anime");
        Assert.Equal("Anime (2)", f3.Name);
    }

    [Fact]
    public async Task RenameFolder_UpdatesPhysicalAndDatabase()
    {
        await _repository.InitializeAsync();

        var f1 = await _folderService.CreateFolderAsync("OldName");
        var allFolders = (await _repository.GetAllFoldersAsync()).ToDictionary(f => f.Id);
        var oldPath = _fileService.GetFolderPath(f1.Id, allFolders);
        Assert.True(Directory.Exists(oldPath));

        var renamed = await _folderService.RenameFolderAsync(f1.Id, "NewName");
        Assert.Equal("NewName", renamed.Name);

        allFolders = (await _repository.GetAllFoldersAsync()).ToDictionary(f => f.Id);
        var newPath = _fileService.GetFolderPath(f1.Id, allFolders);
        Assert.True(Directory.Exists(newPath));
        Assert.False(Directory.Exists(oldPath));
    }

    [Fact]
    public async Task MoveFolder_PreventsCycles()
    {
        await _repository.InitializeAsync();

        var parent = await _folderService.CreateFolderAsync("Parent");
        var child = await _folderService.CreateFolderAsync("Child", parent.Id);
        var grandChild = await _folderService.CreateFolderAsync("GrandChild", child.Id);

        // Attempting to move parent into its descendant must throw InvalidOperationException
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _folderService.MoveFolderAsync(parent.Id, grandChild.Id);
        });

        // Attempting to move into itself must throw InvalidOperationException
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _folderService.MoveFolderAsync(parent.Id, parent.Id);
        });
    }

    [Fact]
    public async Task DeleteFolder_DeletesRecursively()
    {
        await _repository.InitializeAsync();

        var parent = await _folderService.CreateFolderAsync("Parent");
        var child = await _folderService.CreateFolderAsync("Child", parent.Id);

        var score = new ScoreItem("s-child", "Child Song", "MIDI", "", Path.Combine(_storageRoot, "Parent", "Child", "song.mid"), folderId: child.Id);
        await _repository.InsertScoreAsync(score);

        await _folderService.DeleteFolderAsync(parent.Id);

        var folders = await _repository.GetAllFoldersAsync();
        Assert.Empty(folders);

        var scores = await _repository.GetAllScoresAsync();
        Assert.Empty(scores);
    }
}
