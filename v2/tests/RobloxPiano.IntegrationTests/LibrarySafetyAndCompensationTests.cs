using RobloxPiano.Core.Library;
using RobloxPiano.Core.Services;
using RobloxPiano.Infrastructure.Data;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class LibrarySafetyAndCompensationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly string _storageRoot;
    private readonly string _externalDir;

    public LibrarySafetyAndCompensationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"lib_safety_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _dbPath = Path.Combine(_tempDir, "library.db");
        _storageRoot = Path.Combine(_tempDir, "Library");
        _externalDir = Path.Combine(_tempDir, "External");

        Directory.CreateDirectory(_storageRoot);
        Directory.CreateDirectory(_externalDir);
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
    public void IsPathUnderRoot_BoundaryValidation_AccuratelyDistinguishesPrefix()
    {
        var fileService = new LibraryFileService(@"C:\Data\Library");

        // PASS
        Assert.True(fileService.IsPathUnderRoot(@"C:\Data\Library\song.mid"));
        Assert.True(fileService.IsPathUnderRoot(@"C:\Data\Library\Folder\song.mid"));
        Assert.True(fileService.IsPathUnderRoot(@"C:\Data\Library"));

        // FAIL - Prefix match attack cases
        Assert.False(fileService.IsPathUnderRoot(@"C:\Data\LibraryEvil\song.mid"));
        Assert.False(fileService.IsPathUnderRoot(@"C:\Data\Library2\song.mid"));
        Assert.False(fileService.IsPathUnderRoot(@"C:\Data\Other\song.mid"));
        Assert.False(fileService.IsPathUnderRoot(@"C:\Data\Library\..\Other\song.mid"));
    }

    [Fact]
    public void SanitizeName_WindowsReservedNames_TransformedSafely()
    {
        var fileService = new LibraryFileService(_storageRoot);

        Assert.Equal("_CON", fileService.SanitizeName("CON"));
        Assert.Equal("_NUL.mid", fileService.SanitizeName("NUL.mid"));
        Assert.Equal("_AUX.txt", fileService.SanitizeName("AUX.txt"));
        Assert.Equal("Untitled", fileService.SanitizeName("..."));
        Assert.Equal("Untitled", fileService.SanitizeName(""));
        Assert.Equal("CleanSong", fileService.SanitizeName("  CleanSong  "));
        Assert.Equal("SongName", fileService.SanitizeName("Song:Name"));
    }

    [Fact]
    public async Task Import_NonMmlTxtFile_RejectedBeforeCopy()
    {
        var repo = new SqliteLibraryRepository(_dbPath);
        var fileService = new LibraryFileService(_storageRoot);
        var folderService = new FolderService(repo, fileService);
        var libService = new LibraryService(repo, fileService, folderService);

        string readmeFile = Path.Combine(_externalDir, "README.txt");
        await File.WriteAllTextAsync(readmeFile, "This is an ordinary readme documentation file without any notes.");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await libService.ImportExternalFileAsync(readmeFile);
        });

        // Verify no file was copied into managed storage root
        var filesInStorage = Directory.GetFiles(_storageRoot, "*.*", SearchOption.AllDirectories);
        Assert.Empty(filesInStorage);
    }

    [Fact]
    public async Task Import_MalformedMml_SetsAnalysisFailedStatus()
    {
        var repo = new SqliteLibraryRepository(_dbPath);
        var fileService = new LibraryFileService(_storageRoot);
        var folderService = new FolderService(repo, fileService);
        var libService = new LibraryService(repo, fileService, folderService);

        string malformedMml = Path.Combine(_externalDir, "broken.mml");
        await File.WriteAllTextAsync(malformedMml, "MML@t999999999l4c;"); // Out of bounds tempo

        var score = await libService.ImportExternalFileAsync(malformedMml);

        Assert.Equal("ANALYSIS_FAILED", score.AnalysisStatus);
        Assert.False(string.IsNullOrEmpty(score.AnalysisError));
    }

    [Fact]
    public async Task Import_DbFailure_DeletesCopiedOrphanFile()
    {
        var failingRepo = new FailingRepository(_dbPath);
        var fileService = new LibraryFileService(_storageRoot);
        var folderService = new FolderService(failingRepo, fileService);
        var libService = new LibraryService(failingRepo, fileService, folderService);

        string validMml = Path.Combine(_externalDir, "test.mml");
        await File.WriteAllTextAsync(validMml, "MML@t120l4cdef;");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await libService.ImportExternalFileAsync(validMml);
        });

        // Compensation check: copied file must be deleted
        var filesInStorage = Directory.GetFiles(_storageRoot, "*.*", SearchOption.AllDirectories);
        Assert.Empty(filesInStorage);
        Assert.True(File.Exists(validMml)); // Source untouched
    }

    [Fact]
    public async Task RenameScore_DbFailure_RestoresFilename()
    {
        var repo = new SqliteLibraryRepository(_dbPath);
        var fileService = new LibraryFileService(_storageRoot);
        var folderService = new FolderService(repo, fileService);
        var libService = new LibraryService(repo, fileService, folderService);

        string validMml = Path.Combine(_externalDir, "song.mml");
        await File.WriteAllTextAsync(validMml, "MML@t120l4c;");
        var score = await libService.ImportExternalFileAsync(validMml);

        string originalPath = score.FilePath;
        Assert.True(File.Exists(originalPath));

        // Use failing repo for rename
        var failingRepo = new FailingRepository(_dbPath);
        var failingLibService = new LibraryService(failingRepo, fileService, folderService);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await failingLibService.RenameScoreAsync(score.Id, "RenamedFail");
        });

        // Compensation check: filename must be restored to original
        Assert.True(File.Exists(originalPath));
    }

    [Fact]
    public async Task MoveScore_DbFailure_RestoresFileLocation()
    {
        var repo = new SqliteLibraryRepository(_dbPath);
        var fileService = new LibraryFileService(_storageRoot);
        var folderService = new FolderService(repo, fileService);
        var libService = new LibraryService(repo, fileService, folderService);

        string validMml = Path.Combine(_externalDir, "song.mml");
        await File.WriteAllTextAsync(validMml, "MML@t120l4c;");
        var score = await libService.ImportExternalFileAsync(validMml);
        var targetFolder = await folderService.CreateFolderAsync("SubDir");

        string originalPath = score.FilePath;
        Assert.True(File.Exists(originalPath));

        var failingRepo = new FailingRepository(_dbPath);
        var failingLibService = new LibraryService(failingRepo, fileService, folderService);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await failingLibService.MoveScoreAsync(score.Id, targetFolder.Id);
        });

        // Compensation check: file must be restored to original path
        Assert.True(File.Exists(originalPath));
    }

    [Fact]
    public async Task FolderCreate_DbFailure_RemovesEmptyDirectory()
    {
        var failingRepo = new FailingRepository(_dbPath);
        var fileService = new LibraryFileService(_storageRoot);
        var folderService = new FolderService(failingRepo, fileService);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await folderService.CreateFolderAsync("OrphanFolder");
        });

        // Compensation check: directory must be removed
        var createdDir = Path.Combine(_storageRoot, "OrphanFolder");
        Assert.False(Directory.Exists(createdDir));
    }

    [Fact]
    public async Task DestructiveOperations_RejectPathsOutsideV2Root()
    {
        var repo = new SqliteLibraryRepository(_dbPath);
        var fileService = new LibraryFileService(_storageRoot);
        var folderService = new FolderService(repo, fileService);
        var libService = new LibraryService(repo, fileService, folderService);

        // Manually insert a score pointing outside V2 root
        string outsideFile = Path.Combine(_externalDir, "outside.mid");
        await File.WriteAllTextAsync(outsideFile, "DATA");
        var scoreOutside = new ScoreItem("s-ext", "Outside", "MIDI", "", outsideFile, folderId: null);
        await repo.InsertScoreAsync(scoreOutside);

        // Renaming must be blocked
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await libService.RenameScoreAsync(scoreOutside.Id, "OutsideNew");
        });

        // Moving must be blocked
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await libService.MoveScoreAsync(scoreOutside.Id, "target-folder-id");
        });

        // Deleting must be blocked from physical deletion
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await libService.DeleteScoreAsync(scoreOutside.Id);
        });

        Assert.True(File.Exists(outsideFile)); // External file untouched!
    }

    private class FailingRepository : SqliteLibraryRepository
    {
        public FailingRepository(string dbPath) : base(dbPath) { }

        public override Task InsertScoreAsync(ScoreItem score, CancellationToken ct = default)
        {
            throw new InvalidOperationException("Simulated InsertScore DB error.");
        }

        public override Task UpdateScoreAsync(ScoreItem score, CancellationToken ct = default)
        {
            throw new InvalidOperationException("Simulated UpdateScore DB error.");
        }

        public override Task InsertFolderAsync(FolderItem folder, CancellationToken ct = default)
        {
            throw new InvalidOperationException("Simulated InsertFolder DB error.");
        }
    }
}
