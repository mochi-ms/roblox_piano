using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using RobloxPiano.Core.Library;
using RobloxPiano.Core.Services;
using RobloxPiano.Infrastructure.Data;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class V1LibraryMigrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tempV1DbPath;
    private readonly string _tempV2DbPath;
    private readonly string _tempV1StorageRoot;
    private readonly string _tempV2StorageRoot;

    public V1LibraryMigrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"v1_test_migration_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _tempV1DbPath = Path.Combine(_tempDir, "legacy_v1.db");
        _tempV2DbPath = Path.Combine(_tempDir, "library_v2.db");
        _tempV1StorageRoot = Path.Combine(_tempDir, "V1Storage");
        _tempV2StorageRoot = Path.Combine(_tempDir, "V2Storage");

        Directory.CreateDirectory(_tempV1StorageRoot);
        Directory.CreateDirectory(_tempV2StorageRoot);
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

    private async Task<(string File1, string File2)> CreateSyntheticV1FixtureAsync()
    {
        string dir1 = Path.Combine(_tempV1StorageRoot, "Anime", "Ghibli");
        string dir2 = Path.Combine(_tempV1StorageRoot, "Anime");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);

        string file1 = Path.Combine(dir1, "always.mid");
        string file2 = Path.Combine(dir2, "summer.mml");

        await File.WriteAllTextAsync(file1, "SYNTHETIC_MIDI_BINARY_DATA_TEST_12345");
        await File.WriteAllTextAsync(file2, "MML@t120l4cdefgab>c;");

        await using var conn = new SqliteConnection($"Data Source={_tempV1DbPath};Pooling=False;");
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE folders (
                id TEXT PRIMARY KEY,
                parent_id TEXT,
                name TEXT NOT NULL,
                created_at REAL,
                updated_at REAL
            );

            CREATE TABLE scores (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                source_type TEXT,
                source_url TEXT,
                filepath TEXT NOT NULL,
                original_filename TEXT,
                file_extension TEXT,
                folder_id TEXT,
                duration REAL,
                bpm REAL,
                total_notes INTEGER,
                tags TEXT,
                analysis_status TEXT,
                analysis_error TEXT,
                favorite INTEGER,
                created_at REAL,
                updated_at REAL,
                last_played_at REAL
            );

            INSERT INTO folders (id, parent_id, name, created_at, updated_at) VALUES 
            ('f1', NULL, 'Anime', 1700000000.0, 1700000000.0),
            ('f2', 'f1', 'Ghibli', 1700000100.0, 1700000100.0);

            INSERT INTO scores (id, title, source_type, source_url, filepath, original_filename, file_extension, folder_id, duration, bpm, total_notes, tags, analysis_status, favorite, created_at) VALUES
            ('s1', 'Always with Me', 'MIDI', 'url1', @file1, 'always.mid', '.mid', 'f2', 210.0, 95.0, 320, 'ghibli,spirited', 'READY', 1, 1700000200.0),
            ('s2', 'Summer', 'MML', 'url2', @file2, 'summer.mml', '.mml', 'f1', 180.0, 120.0, 250, 'kikujiro', 'READY', 0, 1700000300.0);
        """;
        cmd.Parameters.AddWithValue("@file1", file1);
        cmd.Parameters.AddWithValue("@file2", file2);

        await cmd.ExecuteNonQueryAsync();
        await conn.CloseAsync();
        SqliteConnection.ClearAllPools();

        return (file1, file2);
    }

    private static string ComputeFileSha256(string filePath)
    {
        SqliteConnection.ClearAllPools();
        using var sha = SHA256.Create();
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }

    [Fact]
    public async Task MigrateAsync_StrongIsolation_PreservesV1UntouchedAndCopiesPhysicalFiles()
    {
        var (v1File1, v1File2) = await CreateSyntheticV1FixtureAsync();

        // 1. Record hashes of V1 database and physical files before migration
        string v1DbHashBefore = ComputeFileSha256(_tempV1DbPath);
        string v1File1HashBefore = ComputeFileSha256(v1File1);
        string v1File2HashBefore = ComputeFileSha256(v1File2);

        var v2Repo = new SqliteLibraryRepository(_tempV2DbPath);
        var migrationService = new V1LibraryMigrationService(v2Repo, _tempV1DbPath, _tempV2StorageRoot);

        // 2. Perform Migration
        var result = await migrationService.MigrateAsync();

        Assert.True(result.Success);
        Assert.Equal(2, result.FoldersMigrated);
        Assert.Equal(2, result.ScoresMigrated);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));

        // 3. Verify V1 DB & files remain 100% UNTOUCHED
        string v1DbHashAfter = ComputeFileSha256(_tempV1DbPath);
        string v1File1HashAfter = ComputeFileSha256(v1File1);
        string v1File2HashAfter = ComputeFileSha256(v1File2);

        Assert.Equal(v1DbHashBefore, v1DbHashAfter);
        Assert.Equal(v1File1HashBefore, v1File1HashAfter);
        Assert.Equal(v1File2HashBefore, v1File2HashAfter);
        Assert.True(File.Exists(v1File1));
        Assert.True(File.Exists(v1File2));

        // 4. Verify V2 records point to newly copied V2 managed storage
        var v2FileService = new LibraryFileService(_tempV2StorageRoot);
        var s1 = await v2Repo.GetScoreAsync("s1");
        Assert.NotNull(s1);
        Assert.True(v2FileService.IsPathUnderRoot(s1.FilePath));
        Assert.NotEqual(v1File1, s1.FilePath);
        Assert.True(File.Exists(s1.FilePath));
        Assert.Equal(v1File1HashBefore, ComputeFileSha256(s1.FilePath));
        Assert.Equal(v1File1, s1.SourceUrl); // Legacy V1 path preserved as source URL

        // 5. Destructive operations on V2 must NEVER affect V1
        var v2FolderService = new FolderService(v2Repo, v2FileService);
        var v2LibService = new LibraryService(v2Repo, v2FileService, v2FolderService);

        // A. Rename score in V2
        var renamed = await v2LibService.RenameScoreAsync(s1.Id, "Always_Renamed");
        Assert.Equal("Always_Renamed", renamed.Title);
        Assert.True(File.Exists(renamed.FilePath));
        Assert.True(File.Exists(v1File1)); // V1 untouched!

        // B. Move score in V2
        await v2LibService.MoveScoreAsync(s1.Id, "f1");
        var moved = await v2Repo.GetScoreAsync(s1.Id);
        Assert.NotNull(moved);
        Assert.Equal("f1", moved.FolderId);
        Assert.True(File.Exists(moved.FilePath));
        Assert.True(File.Exists(v1File1)); // V1 untouched!

        // C. Delete score in V2
        await v2LibService.DeleteScoreAsync(s1.Id);
        Assert.Null(await v2Repo.GetScoreAsync(s1.Id));
        Assert.False(File.Exists(moved.FilePath)); // V2 file deleted
        Assert.True(File.Exists(v1File1)); // V1 file STILL EXISTS and is untouched!
    }

    [Fact]
    public async Task MigrateAsync_AtomicRollback_LeavesZeroV2RowsOnFailure()
    {
        await CreateSyntheticV1FixtureAsync();

        // Create failing repository wrapper
        var failingRepo = new FailingBulkImportRepository(_tempV2DbPath);
        var migrationService = new V1LibraryMigrationService(failingRepo, _tempV1DbPath, _tempV2StorageRoot);

        var result = await migrationService.MigrateAsync();

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);

        // Verify V2 database has ZERO rows
        var realRepo = new SqliteLibraryRepository(_tempV2DbPath);
        var allFolders = await realRepo.GetAllFoldersAsync();
        var allScores = await realRepo.GetAllScoresAsync();

        Assert.Empty(allFolders);
        Assert.Empty(allScores);

        // Verify V2 storage has NO orphan copied files
        var v2Files = Directory.GetFiles(_tempV2StorageRoot, "*.*", SearchOption.AllDirectories);
        Assert.Empty(v2Files);
    }

    [Fact]
    public async Task MigrateAsync_Idempotency_RunningTwiceDoesNotDuplicateManagedFiles()
    {
        await CreateSyntheticV1FixtureAsync();

        var v2Repo = new SqliteLibraryRepository(_tempV2DbPath);
        var migrationService = new V1LibraryMigrationService(v2Repo, _tempV1DbPath, _tempV2StorageRoot);

        // Run #1
        var res1 = await migrationService.MigrateAsync();
        Assert.True(res1.Success);

        int fileCountAfterFirst = Directory.GetFiles(_tempV2StorageRoot, "*.*", SearchOption.AllDirectories).Length;
        Assert.Equal(2, fileCountAfterFirst);

        // Run #2
        var res2 = await migrationService.MigrateAsync();
        Assert.True(res2.Success);

        int fileCountAfterSecond = Directory.GetFiles(_tempV2StorageRoot, "*.*", SearchOption.AllDirectories).Length;
        Assert.Equal(2, fileCountAfterSecond); // No duplicated Song (1).mid!
    }

    private class FailingBulkImportRepository : SqliteLibraryRepository
    {
        public FailingBulkImportRepository(string dbPath) : base(dbPath) { }

        public override Task BulkImportAsync(IReadOnlyList<FolderItem> folders, IReadOnlyList<ScoreItem> scores, CancellationToken ct = default)
        {
            throw new InvalidOperationException("Simulated mid-migration transactional failure.");
        }
    }
}
