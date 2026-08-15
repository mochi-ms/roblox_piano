using System.Security.Cryptography;
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

    private static string ComputeFileSha256(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(sha.ComputeHash(stream));
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
    public async Task RenameFolder_ScoreDiscoveryFailure_RollsPhysicalFolderBack()
    {
        await _repository.InitializeAsync();
        var f1 = await _folderService.CreateFolderAsync("AnimeFolder");

        var allFolders = (await _repository.GetAllFoldersAsync()).ToDictionary(f => f.Id);
        var oldPath = _fileService.GetFolderPath(f1.Id, allFolders);
        Assert.True(Directory.Exists(oldPath));

        // Use failing test double that fails on score queries during recursion
        var failingRepo = new FailingScoreDiscoveryRepository(_dbPath);
        var failingFolderService = new FolderService(failingRepo, _fileService);

        var targetNewPath = Path.Combine(_storageRoot, "AnimeRenamed");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await failingFolderService.RenameFolderAsync(f1.Id, "AnimeRenamed");
        });

        // Verify compensation: physical directory rolled back to oldPath, targetNewPath does not exist
        Assert.True(Directory.Exists(oldPath));
        Assert.False(Directory.Exists(targetNewPath));

        // Verify DB unchanged
        var folderInDb = await _repository.GetFolderAsync(f1.Id);
        Assert.NotNull(folderInDb);
        Assert.Equal("AnimeFolder", folderInDb.Name);
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
    public async Task MoveFolder_ScoreDiscoveryFailure_RollsPhysicalFolderBack()
    {
        await _repository.InitializeAsync();
        var targetParent = await _folderService.CreateFolderAsync("TargetParent");
        var child = await _folderService.CreateFolderAsync("ChildFolder");

        var allFolders = (await _repository.GetAllFoldersAsync()).ToDictionary(f => f.Id);
        var oldChildPath = _fileService.GetFolderPath(child.Id, allFolders);
        Assert.True(Directory.Exists(oldChildPath));

        var failingRepo = new FailingScoreDiscoveryRepository(_dbPath);
        var failingFolderService = new FolderService(failingRepo, _fileService);

        var targetNewPath = Path.Combine(_storageRoot, "TargetParent", "ChildFolder");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await failingFolderService.MoveFolderAsync(child.Id, targetParent.Id);
        });

        // Verify compensation: physical directory rolled back to oldChildPath, targetNewPath does not exist
        Assert.True(Directory.Exists(oldChildPath));
        Assert.False(Directory.Exists(targetNewPath));

        // Verify DB unchanged
        var childInDb = await _repository.GetFolderAsync(child.Id);
        Assert.NotNull(childInDb);
        Assert.Null(childInDb.ParentId);
    }

    [Fact]
    public async Task DeleteFolder_DeletesRecursively()
    {
        await _repository.InitializeAsync();

        var parent = await _folderService.CreateFolderAsync("Parent");
        var child = await _folderService.CreateFolderAsync("Child", parent.Id);

        string file1 = Path.Combine(_storageRoot, "Parent", "parent_song.mid");
        string file2 = Path.Combine(_storageRoot, "Parent", "Child", "child_song.mid");
        await File.WriteAllTextAsync(file1, "REAL_PARENT_MIDI_BYTES");
        await File.WriteAllTextAsync(file2, "REAL_CHILD_MIDI_BYTES");

        var score1 = new ScoreItem("s-parent", "Parent Song", "MIDI", "", file1, folderId: parent.Id);
        var score2 = new ScoreItem("s-child", "Child Song", "MIDI", "", file2, folderId: child.Id);
        await _repository.InsertScoreAsync(score1);
        await _repository.InsertScoreAsync(score2);

        Assert.True(File.Exists(file1));
        Assert.True(File.Exists(file2));

        await _folderService.DeleteFolderAsync(parent.Id);

        // Verify DB rows removed
        var folders = await _repository.GetAllFoldersAsync();
        Assert.Empty(folders);

        var scores = await _repository.GetAllScoresAsync();
        Assert.Empty(scores);

        // Verify physical files and directories removed
        Assert.False(File.Exists(file1));
        Assert.False(File.Exists(file2));
        Assert.False(Directory.Exists(Path.Combine(_storageRoot, "Parent")));

        // Verify staging directory is clean
        var stagingDir = Path.Combine(_storageRoot, ".delete-staging");
        Assert.False(Directory.Exists(stagingDir));
    }

    [Fact]
    public async Task DeleteFolder_DbFailure_RestoresEntirePhysicalAndDatabaseHierarchy()
    {
        await _repository.InitializeAsync();

        // Setup deep hierarchy:
        // Parent
        // ├─ ParentScore.mid
        // ├─ ChildA
        // │  ├─ A.mid
        // │  └─ GrandChild
        // │     └─ G.mml
        // └─ ChildB
        //    └─ B.mid
        var parent = await _folderService.CreateFolderAsync("Parent");
        var childA = await _folderService.CreateFolderAsync("ChildA", parent.Id);
        var grandChild = await _folderService.CreateFolderAsync("GrandChild", childA.Id);
        var childB = await _folderService.CreateFolderAsync("ChildB", parent.Id);

        string fParent = Path.Combine(_storageRoot, "Parent", "ParentScore.mid");
        string fA = Path.Combine(_storageRoot, "Parent", "ChildA", "A.mid");
        string fG = Path.Combine(_storageRoot, "Parent", "ChildA", "GrandChild", "G.mml");
        string fB = Path.Combine(_storageRoot, "Parent", "ChildB", "B.mid");

        await File.WriteAllTextAsync(fParent, "PARENT_SCORE_CONTENT_TEST");
        await File.WriteAllTextAsync(fA, "A_SCORE_CONTENT_TEST");
        await File.WriteAllTextAsync(fG, "MML@t120l4cdef;");
        await File.WriteAllTextAsync(fB, "B_SCORE_CONTENT_TEST");

        var sParent = new ScoreItem("s-p", "Parent Track", "MIDI", "", fParent, folderId: parent.Id);
        var sA = new ScoreItem("s-a", "A Track", "MIDI", "", fA, folderId: childA.Id);
        var sG = new ScoreItem("s-g", "G Track", "MML", "", fG, folderId: grandChild.Id);
        var sB = new ScoreItem("s-b", "B Track", "MIDI", "", fB, folderId: childB.Id);

        await _repository.InsertScoreAsync(sParent);
        await _repository.InsertScoreAsync(sA);
        await _repository.InsertScoreAsync(sG);
        await _repository.InsertScoreAsync(sB);

        string hashParent = ComputeFileSha256(fParent);
        string hashA = ComputeFileSha256(fA);
        string hashG = ComputeFileSha256(fG);
        string hashB = ComputeFileSha256(fB);

        // Inject transactional DB failure
        var failingRepo = new FailingFolderTreeDeleteRepository(_dbPath);
        var failingFolderService = new FolderService(failingRepo, _fileService);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await failingFolderService.DeleteFolderAsync(parent.Id);
        });

        // 1. Verify all physical files and directories restored at original paths
        Assert.True(Directory.Exists(Path.Combine(_storageRoot, "Parent")));
        Assert.True(Directory.Exists(Path.Combine(_storageRoot, "Parent", "ChildA")));
        Assert.True(Directory.Exists(Path.Combine(_storageRoot, "Parent", "ChildA", "GrandChild")));
        Assert.True(Directory.Exists(Path.Combine(_storageRoot, "Parent", "ChildB")));

        Assert.True(File.Exists(fParent));
        Assert.True(File.Exists(fA));
        Assert.True(File.Exists(fG));
        Assert.True(File.Exists(fB));

        // 2. Verify SHA256 hashes are identical
        Assert.Equal(hashParent, ComputeFileSha256(fParent));
        Assert.Equal(hashA, ComputeFileSha256(fA));
        Assert.Equal(hashG, ComputeFileSha256(fG));
        Assert.Equal(hashB, ComputeFileSha256(fB));

        // 3. Verify all DB records remain intact
        var folders = await _repository.GetAllFoldersAsync();
        Assert.Equal(4, folders.Count);

        var scores = await _repository.GetAllScoresAsync();
        Assert.Equal(4, scores.Count);

        // 4. Verify no staging orphan remains
        var stagingDir = Path.Combine(_storageRoot, ".delete-staging");
        Assert.False(Directory.Exists(stagingDir));
    }

    [Fact]
    public async Task DeleteFolder_SiblingSubtreeRemainsUntouched()
    {
        await _repository.InitializeAsync();

        var anime = await _folderService.CreateFolderAsync("Anime");
        var classical = await _folderService.CreateFolderAsync("Classical");

        string animeFile = Path.Combine(_storageRoot, "Anime", "delete-this.mid");
        string classicalFile = Path.Combine(_storageRoot, "Classical", "keep-this.mid");

        await File.WriteAllTextAsync(animeFile, "ANIME_SONG_BYTES");
        await File.WriteAllTextAsync(classicalFile, "CLASSICAL_SONG_BYTES_KEEP");

        var sAnime = new ScoreItem("s-anime", "Anime Song", "MIDI", "", animeFile, folderId: anime.Id);
        var sClassical = new ScoreItem("s-class", "Classical Song", "MIDI", "", classicalFile, folderId: classical.Id);

        await _repository.InsertScoreAsync(sAnime);
        await _repository.InsertScoreAsync(sClassical);

        string classicalHashBefore = ComputeFileSha256(classicalFile);

        // Delete Anime
        await _folderService.DeleteFolderAsync(anime.Id);

        // Verify Classical folder and file untouched
        Assert.True(Directory.Exists(Path.Combine(_storageRoot, "Classical")));
        Assert.True(File.Exists(classicalFile));
        Assert.Equal(classicalHashBefore, ComputeFileSha256(classicalFile));

        Assert.NotNull(await _repository.GetFolderAsync(classical.Id));
        Assert.NotNull(await _repository.GetScoreAsync(sClassical.Id));

        // Verify Anime removed
        Assert.Null(await _repository.GetFolderAsync(anime.Id));
        Assert.Null(await _repository.GetScoreAsync(sAnime.Id));
        Assert.False(Directory.Exists(Path.Combine(_storageRoot, "Anime")));
    }

    [Fact]
    public async Task DeleteFolder_ExternalScorePath_RejectsBeforeMutation()
    {
        await _repository.InitializeAsync();

        var targetFolder = await _folderService.CreateFolderAsync("TargetFolder");
        string managedFile = Path.Combine(_storageRoot, "TargetFolder", "managed.mid");
        await File.WriteAllTextAsync(managedFile, "MANAGED_FILE_CONTENT");

        // External file outside V2 managed storage
        string externalDir = Path.Combine(_tempDir, "ExternalDocs");
        Directory.CreateDirectory(externalDir);
        string externalFile = Path.Combine(externalDir, "important.mid");
        await File.WriteAllTextAsync(externalFile, "IMPORTANT_EXTERNAL_CONTENT");
        string externalHashBefore = ComputeFileSha256(externalFile);

        var scoreManaged = new ScoreItem("s-man", "Managed Score", "MIDI", "", managedFile, folderId: targetFolder.Id);
        var scoreExternal = new ScoreItem("s-ext", "External Score", "MIDI", "", externalFile, folderId: targetFolder.Id);

        await _repository.InsertScoreAsync(scoreManaged);
        await _repository.InsertScoreAsync(scoreExternal);

        // Attempting to delete targetFolder must reject before mutation
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _folderService.DeleteFolderAsync(targetFolder.Id);
        });

        // Verify external file untouched
        Assert.True(File.Exists(externalFile));
        Assert.Equal(externalHashBefore, ComputeFileSha256(externalFile));

        // Verify target folder, managed file, and DB records untouched
        Assert.True(Directory.Exists(Path.Combine(_storageRoot, "TargetFolder")));
        Assert.True(File.Exists(managedFile));
        Assert.NotNull(await _repository.GetFolderAsync(targetFolder.Id));
        Assert.NotNull(await _repository.GetScoreAsync(scoreManaged.Id));
        Assert.NotNull(await _repository.GetScoreAsync(scoreExternal.Id));
    }

    [Fact]
    public async Task DeleteFolder_FtsSearchResultsRemoved()
    {
        await _repository.InitializeAsync();

        var folder = await _folderService.CreateFolderAsync("AnimeFolder");
        string songFile = Path.Combine(_storageRoot, "AnimeFolder", "song.mid");
        await File.WriteAllTextAsync(songFile, "SONG_DATA");

        var score = new ScoreItem("s-delete-me", "Delete Me Anime Song", "MIDI", "", songFile, tags: "ghibli,delete", folderId: folder.Id);
        await _repository.InsertScoreAsync(score);

        // Verify searchable before delete
        var searchBefore = await _repository.QueryScoresAsync(new LibraryQuery { SearchKeyword = "Delete Me" });
        Assert.Single(searchBefore.Items);

        // Delete folder
        await _folderService.DeleteFolderAsync(folder.Id);

        // Verify FTS returns 0 results
        var searchAfter = await _repository.QueryScoresAsync(new LibraryQuery { SearchKeyword = "Delete Me" });
        Assert.Empty(searchAfter.Items);
    }

    private class FailingScoreDiscoveryRepository : SqliteLibraryRepository
    {
        public FailingScoreDiscoveryRepository(string dbPath) : base(dbPath) { }

        public override Task<LibraryPage> QueryScoresAsync(LibraryQuery query, CancellationToken ct = default)
        {
            if (!string.IsNullOrEmpty(query.FolderId))
            {
                throw new InvalidOperationException("Simulated score discovery failure after physical directory move.");
            }
            return base.QueryScoresAsync(query, ct);
        }
    }

    private class FailingFolderTreeDeleteRepository : SqliteLibraryRepository
    {
        public FailingFolderTreeDeleteRepository(string dbPath) : base(dbPath) { }

        public override Task DeleteFolderTreeAsync(IReadOnlyList<string> scoreIds, IReadOnlyList<string> folderIds, CancellationToken ct = default)
        {
            throw new InvalidOperationException("Simulated transactional DB error during folder tree deletion.");
        }
    }
}
