using RobloxPiano.Core.Library;

namespace RobloxPiano.Infrastructure.Data;

public class V1LibraryMigrationResult
{
    public bool Success { get; set; }
    public int FoldersMigrated { get; set; }
    public int ScoresMigrated { get; set; }
    public string? BackupPath { get; set; }
    public string? ErrorMessage { get; set; }
}

public class V1LibraryMigrationService
{
    private readonly ILibraryRepository _v2Repository;
    private readonly string _v1DatabasePath;

    public V1LibraryMigrationService(ILibraryRepository v2Repository, string? v1DatabasePath = null)
    {
        _v2Repository = v2Repository;
        _v1DatabasePath = v1DatabasePath ?? LibraryDatabasePathProvider.GetDefaultLegacyV1DatabasePath();
    }

    public async Task<V1LibraryMigrationResult> MigrateAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_v1DatabasePath))
        {
            return new V1LibraryMigrationResult
            {
                Success = true,
                FoldersMigrated = 0,
                ScoresMigrated = 0,
                BackupPath = null,
                ErrorMessage = "V1 database does not exist."
            };
        }

        // 1. Create byte-for-byte backup copy of V1 database
        string backupDir = Path.GetDirectoryName(_v1DatabasePath) ?? AppDomain.CurrentDomain.BaseDirectory;
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        string backupPath = Path.Combine(backupDir, $"library_v1_{timestamp}.bak");

        try
        {
            File.Copy(_v1DatabasePath, backupPath, overwrite: true);
            if (!File.Exists(backupPath) || new FileInfo(backupPath).Length == 0)
            {
                throw new InvalidOperationException($"Failed to create valid backup at {backupPath}");
            }
        }
        catch (Exception ex)
        {
            return new V1LibraryMigrationResult
            {
                Success = false,
                ErrorMessage = $"V1 backup creation failed: {ex.Message}"
            };
        }

        // 2. Read V1 database read-only
        var reader = new V1ReadOnlyLibraryReader(_v1DatabasePath);
        IReadOnlyList<FolderItem> folders;
        IReadOnlyList<ScoreItem> scores;

        try
        {
            (folders, scores) = await reader.ReadAllAsync(ct);
        }
        catch (Exception ex)
        {
            return new V1LibraryMigrationResult
            {
                Success = false,
                BackupPath = backupPath,
                ErrorMessage = $"Failed reading V1 database: {ex.Message}"
            };
        }

        // 3. Write into V2 repository
        int foldersCount = 0;
        int scoresCount = 0;

        try
        {
            await _v2Repository.InitializeAsync(ct);

            foreach (var folder in folders)
            {
                await _v2Repository.InsertFolderAsync(folder, ct);
                foldersCount++;
            }

            foreach (var score in scores)
            {
                await _v2Repository.InsertScoreAsync(score, ct);
                scoresCount++;
            }

            return new V1LibraryMigrationResult
            {
                Success = true,
                FoldersMigrated = foldersCount,
                ScoresMigrated = scoresCount,
                BackupPath = backupPath
            };
        }
        catch (Exception ex)
        {
            return new V1LibraryMigrationResult
            {
                Success = false,
                FoldersMigrated = foldersCount,
                ScoresMigrated = scoresCount,
                BackupPath = backupPath,
                ErrorMessage = $"Failed writing into V2 repository: {ex.Message}"
            };
        }
    }
}
