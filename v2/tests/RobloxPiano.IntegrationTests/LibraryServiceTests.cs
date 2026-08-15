using RobloxPiano.Core.Library;
using RobloxPiano.Core.Services;
using RobloxPiano.Infrastructure.Data;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class LibraryServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly string _storageRoot;
    private readonly string _sourceDir;
    private readonly SqliteLibraryRepository _repository;
    private readonly LibraryFileService _fileService;
    private readonly FolderService _folderService;
    private readonly LibraryService _libraryService;

    public LibraryServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"lib_svc_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _dbPath = Path.Combine(_tempDir, "library.db");
        _storageRoot = Path.Combine(_tempDir, "Storage");
        _sourceDir = Path.Combine(_tempDir, "ExternalSource");

        Directory.CreateDirectory(_storageRoot);
        Directory.CreateDirectory(_sourceDir);

        _repository = new SqliteLibraryRepository(_dbPath);
        _fileService = new LibraryFileService(_storageRoot);
        _folderService = new FolderService(_repository, _fileService);
        _libraryService = new LibraryService(_repository, _fileService, _folderService);
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
    public async Task ImportExternalFile_MmlFile_ExtractsMetadataAndCopiesSafely()
    {
        await _repository.InitializeAsync();

        string sourceMml = Path.Combine(_sourceDir, "test_song.mml");
        string mmlContent = "MML@t140l4cdefgab>c,l4o3cdefgab>c;";
        await File.WriteAllTextAsync(sourceMml, mmlContent);

        var score = await _libraryService.ImportExternalFileAsync(sourceMml);

        Assert.NotNull(score);
        Assert.Equal("test_song", score.Title);
        Assert.Equal("MML", score.SourceType);
        Assert.Equal(140.0, score.Bpm);
        Assert.True(score.TotalNotes > 0);
        Assert.True(File.Exists(score.FilePath));
        Assert.True(File.Exists(sourceMml)); // Original source remains 100% intact!
    }

    [Fact]
    public async Task ImportFolderRecursive_ImportsHierarchyAndSkipsNonScores()
    {
        await _repository.InitializeAsync();

        // Create directory structure in sourceDir
        string subDir1 = Path.Combine(_sourceDir, "GameMusic");
        string subDir2 = Path.Combine(subDir1, "Touhou");
        Directory.CreateDirectory(subDir2);

        // Scores
        await File.WriteAllTextAsync(Path.Combine(subDir1, "boss.mml"), "MML@t160l8cdef;");
        await File.WriteAllTextAsync(Path.Combine(subDir2, "badapple.mml"), "MML@t138l4cdef;");

        // Non-scores / junk files
        await File.WriteAllTextAsync(Path.Combine(subDir1, "desktop.ini"), "junk");
        await File.WriteAllTextAsync(Path.Combine(subDir2, "readme.txt"), "regular readme text without mml header");

        var summary = await _libraryService.ImportFolderRecursiveAsync(_sourceDir);

        Assert.False(summary.Cancelled);
        Assert.Equal(2, summary.ImportedScores);
        Assert.True(summary.Skipped >= 2);

        var allFolders = await _repository.GetAllFoldersAsync();
        Assert.Contains(allFolders, f => f.Name == "GameMusic");
        Assert.Contains(allFolders, f => f.Name == "Touhou");

        var allScores = await _repository.GetAllScoresAsync();
        Assert.Equal(2, allScores.Count);
        Assert.Contains(allScores, s => s.Title == "boss");
        Assert.Contains(allScores, s => s.Title == "badapple");
    }

    [Fact]
    public async Task ScoreCRUD_RenameCopyMoveDelete_WorkAccurately()
    {
        await _repository.InitializeAsync();

        string sourceMml = Path.Combine(_sourceDir, "song.mml");
        await File.WriteAllTextAsync(sourceMml, "MML@t120l4c;");

        var score = await _libraryService.ImportExternalFileAsync(sourceMml);
        Assert.Equal("song", score.Title);

        // 1. Rename
        var renamed = await _libraryService.RenameScoreAsync(score.Id, "SongRenamed");
        Assert.Equal("SongRenamed", renamed.Title);
        Assert.True(File.Exists(renamed.FilePath));
        Assert.EndsWith("SongRenamed.mml", renamed.FilePath);

        // 2. Folder & Move
        var folder = await _folderService.CreateFolderAsync("SubFolder");
        await _libraryService.MoveScoreAsync(renamed.Id, folder.Id);
        var moved = await _repository.GetScoreAsync(renamed.Id);
        Assert.NotNull(moved);
        Assert.Equal(folder.Id, moved.FolderId);
        Assert.True(File.Exists(moved.FilePath));

        // 3. Copy
        var copy = await _libraryService.CopyScoreAsync(renamed.Id, null);
        Assert.NotNull(copy);
        Assert.NotEqual(renamed.Id, copy.Id);
        Assert.Null(copy.FolderId);
        Assert.True(File.Exists(copy.FilePath));

        // 4. Delete
        await _libraryService.DeleteScoreAsync(copy.Id);
        var deletedCopy = await _repository.GetScoreAsync(copy.Id);
        Assert.Null(deletedCopy);
        Assert.False(File.Exists(copy.FilePath));
    }
}
