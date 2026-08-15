using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using RobloxPiano.Core.Library;
using RobloxPiano.Infrastructure.Data;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class V1LibraryMigrationTests : IDisposable
{
    private readonly string _tempV1DbPath;
    private readonly string _tempV2DbPath;
    private readonly string _tempDir;

    public V1LibraryMigrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"v1_test_migration_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _tempV1DbPath = Path.Combine(_tempDir, "legacy_v1.db");
        _tempV2DbPath = Path.Combine(_tempDir, "library_v2.db");
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

    private async Task CreateSyntheticV1DatabaseAsync()
    {
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
            ('s1', 'Always with Me', 'MIDI', 'url1', 'C:\songs\always.mid', 'always.mid', '.mid', 'f2', 210.0, 95.0, 320, 'ghibli,spirited', 'READY', 1, 1700000200.0),
            ('s2', 'Summer', 'MML', 'url2', 'C:\songs\summer.mml', 'summer.mml', '.mml', 'f1', 180.0, 120.0, 250, 'kikujiro', 'READY', 0, 1700000300.0);
        """;
        await cmd.ExecuteNonQueryAsync();
        await conn.CloseAsync();
        SqliteConnection.ClearAllPools();
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
    public async Task MigrateAsync_PreservesV1UntouchedAndMigratesAllData()
    {
        await CreateSyntheticV1DatabaseAsync();

        // 1. Compute hash of V1 database before migration
        string hashBefore = ComputeFileSha256(_tempV1DbPath);

        var v2Repo = new SqliteLibraryRepository(_tempV2DbPath);
        var migrationService = new V1LibraryMigrationService(v2Repo, _tempV1DbPath);

        // 2. Perform Migration
        var result = await migrationService.MigrateAsync();

        Assert.True(result.Success);
        Assert.Equal(2, result.FoldersMigrated);
        Assert.Equal(2, result.ScoresMigrated);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));

        // 3. Verify V1 hash is 100% UNCHANGED (Zero writes to V1 source)
        string hashAfter = ComputeFileSha256(_tempV1DbPath);
        Assert.Equal(hashBefore, hashAfter);

        // 4. Verify V2 data
        var allFolders = await v2Repo.GetAllFoldersAsync();
        Assert.Equal(2, allFolders.Count);
        Assert.Contains(allFolders, f => f.Id == "f1" && f.Name == "Anime");
        Assert.Contains(allFolders, f => f.Id == "f2" && f.Name == "Ghibli" && f.ParentId == "f1");

        var allScores = await v2Repo.GetAllScoresAsync();
        Assert.Equal(2, allScores.Count);

        var s1 = allScores.FirstOrDefault(s => s.Id == "s1");
        Assert.NotNull(s1);
        Assert.Equal("Always with Me", s1.Title);
        Assert.Equal("MIDI", s1.SourceType);
        Assert.Equal(210.0, s1.Duration);
        Assert.True(s1.Favorite);
        Assert.Equal("f2", s1.FolderId);

        var s2 = allScores.FirstOrDefault(s => s.Id == "s2");
        Assert.NotNull(s2);
        Assert.Equal("Summer", s2.Title);
        Assert.Equal("MML", s2.SourceType);
        Assert.Equal(180.0, s2.Duration);
        Assert.False(s2.Favorite);
    }
}
