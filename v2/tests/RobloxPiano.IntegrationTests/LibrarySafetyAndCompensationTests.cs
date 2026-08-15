using System.Security.Cryptography;
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

    private static string ComputeFileSha256(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(sha.ComputeHash(stream));
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

    [Theory]
    [InlineData("README")]
    [InlineData("Step 1")]
    [InlineData("Chapter 2")]
    [InlineData("Version 3")]
    [InlineData("1")]
    [InlineData("1 2 3 4 5")]
    [InlineData("C# programming guide")]
    [InlineData("Roblox Piano Player v2")]
    [InlineData("")]
    [InlineData("   \t\r\n  ")]
    public async Task TxtImport_OrdinaryNumberedText_IsRejectedBeforeCopy(string plainContent)
    {
        var repo = new SqliteLibraryRepository(_dbPath);
        var fileService = new LibraryFileService(_storageRoot);
        var folderService = new FolderService(repo, fileService);
        var libService = new LibraryService(repo, fileService, folderService);

        string txtFile = Path.Combine(_externalDir, $"test_{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(txtFile, plainContent);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await libService.ImportExternalFileAsync(txtFile);
        });

        // Verify no file was copied into managed storage root
        var filesInStorage = Directory.GetFiles(_storageRoot, "*.*", SearchOption.AllDirectories);
        Assert.Empty(filesInStorage);
    }

    [Fact]
    public async Task TxtImport_ValidMml_IsAccepted()
    {
        var repo = new SqliteLibraryRepository(_dbPath);
        var fileService = new LibraryFileService(_storageRoot);
        var folderService = new FolderService(repo, fileService);
        var libService = new LibraryService(repo, fileService, folderService);

        string validMmlTxt = Path.Combine(_externalDir, "valid_song.txt");
        await File.WriteAllTextAsync(validMmlTxt, "MML@t120l4cdefgab>c;");

        var score = await libService.ImportExternalFileAsync(validMmlTxt);

        Assert.NotNull(score);
        Assert.Equal("MML", score.SourceType);
        Assert.Equal(8, score.TotalNotes);
        Assert.True(File.Exists(score.FilePath));
        Assert.True(fileService.IsPathUnderRoot(score.FilePath));
    }

    [Fact]
    public async Task MmlImport_InvalidSyntax_IsRejectedBeforeCopy()
    {
        var repo = new SqliteLibraryRepository(_dbPath);
        var fileService = new LibraryFileService(_storageRoot);
        var folderService = new FolderService(repo, fileService);
        var libService = new LibraryService(repo, fileService, folderService);

        string brokenMml = Path.Combine(_externalDir, "broken.mml");
        await File.WriteAllTextAsync(brokenMml, "THIS IS INVALID MML WITHOUT NOTES");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await libService.ImportExternalFileAsync(brokenMml);
        });

        // Verify no file was copied into managed storage root
        var filesInStorage = Directory.GetFiles(_storageRoot, "*.*", SearchOption.AllDirectories);
        Assert.Empty(filesInStorage);
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
    public async Task CopyScore_DbFailure_DeletesCopiedFileAndPreservesSource()
    {
        var repo = new SqliteLibraryRepository(_dbPath);
        var fileService = new LibraryFileService(_storageRoot);
        var folderService = new FolderService(repo, fileService);
        var libService = new LibraryService(repo, fileService, folderService);

        // 1. Create valid managed source score
        string validMml = Path.Combine(_externalDir, "original.mml");
        await File.WriteAllTextAsync(validMml, "MML@t120l4cdefgab;");
        var sourceScore = await libService.ImportExternalFileAsync(validMml);

        string sourcePath = sourceScore.FilePath;
        string sourceHashBefore = ComputeFileSha256(sourcePath);
        var subFolder = await folderService.CreateFolderAsync("SubDir");

        // 2. Use failing repo for copy insert
        var failingRepo = new FailingCopyRepository(_dbPath);
        var failingLibService = new LibraryService(failingRepo, fileService, folderService);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await failingLibService.CopyScoreAsync(sourceScore.Id, subFolder.Id);
        });

        // 3. Verify compensation: copied file deleted, source file and record preserved
        var subFiles = Directory.GetFiles(Path.Combine(_storageRoot, "SubDir"), "*.*", SearchOption.AllDirectories);
        Assert.Empty(subFiles);

        Assert.True(File.Exists(sourcePath));
        Assert.Equal(sourceHashBefore, ComputeFileSha256(sourcePath));
        Assert.NotNull(await repo.GetScoreAsync(sourceScore.Id));
    }

    [Fact]
    public async Task CopyScore_ExternalSourcePath_IsRejected()
    {
        var repo = new SqliteLibraryRepository(_dbPath);
        var fileService = new LibraryFileService(_storageRoot);
        var folderService = new FolderService(repo, fileService);
        var libService = new LibraryService(repo, fileService, folderService);

        // Manually insert score pointing outside V2 managed storage
        string outsideFile = Path.Combine(_externalDir, "outside_source.mid");
        await File.WriteAllTextAsync(outsideFile, "RAW_MIDI_BYTES");
        var scoreOutside = new ScoreItem("s-ext-copy", "OutsideCopy", "MIDI", "", outsideFile);
        await repo.InsertScoreAsync(scoreOutside);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await libService.CopyScoreAsync(scoreOutside.Id, null);
        });

        // No copy made inside V2 storage
        var filesInStorage = Directory.GetFiles(_storageRoot, "*.*", SearchOption.AllDirectories);
        Assert.Empty(filesInStorage);
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
    public async Task DeleteScore_DbFailure_RestoresManagedFile()
    {
        var repo = new SqliteLibraryRepository(_dbPath);
        var fileService = new LibraryFileService(_storageRoot);
        var folderService = new FolderService(repo, fileService);
        var libService = new LibraryService(repo, fileService, folderService);

        string validMml = Path.Combine(_externalDir, "to_delete.mml");
        await File.WriteAllTextAsync(validMml, "MML@t120l4cdef;");
        var score = await libService.ImportExternalFileAsync(validMml);

        string originalPath = score.FilePath;
        Assert.True(File.Exists(originalPath));

        var failingRepo = new FailingDeleteRepository(_dbPath);
        var failingLibService = new LibraryService(failingRepo, fileService, folderService);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await failingLibService.DeleteScoreAsync(score.Id);
        });

        // Compensation check: physical file must be restored
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

    private class FailingCopyRepository : SqliteLibraryRepository
    {
        public FailingCopyRepository(string dbPath) : base(dbPath) { }

        public override Task InsertScoreAsync(ScoreItem score, CancellationToken ct = default)
        {
            throw new InvalidOperationException("Simulated Copy InsertScore DB error.");
        }
    }

    private class FailingDeleteRepository : SqliteLibraryRepository
    {
        public FailingDeleteRepository(string dbPath) : base(dbPath) { }

        public override Task DeleteScoreAsync(string scoreId, CancellationToken ct = default)
        {
            throw new InvalidOperationException("Simulated DeleteScore DB error.");
        }
    }
}
