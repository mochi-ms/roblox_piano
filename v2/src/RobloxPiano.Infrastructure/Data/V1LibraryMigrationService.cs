using System.Security.Cryptography;
using RobloxPiano.Core.Library;
using RobloxPiano.Core.Services;

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
    private readonly string _v2StorageRoot;
    private readonly LibraryFileService _fileService;

    public V1LibraryMigrationService(
        ILibraryRepository v2Repository,
        string? v1DatabasePath = null,
        string? v2StorageRoot = null)
    {
        _v2Repository = v2Repository;
        _v1DatabasePath = v1DatabasePath ?? LibraryDatabasePathProvider.GetDefaultLegacyV1DatabasePath();
        _v2StorageRoot = v2StorageRoot ?? LibraryDatabasePathProvider.GetDefaultLibraryStorageRoot();
        _fileService = new LibraryFileService(_v2StorageRoot);
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

        // 1. Create collision-safe byte-for-byte backup copy of V1 database
        string backupDir = Path.GetDirectoryName(_v1DatabasePath) ?? AppDomain.CurrentDomain.BaseDirectory;
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        string backupPath = Path.Combine(backupDir, $"library_v1_{timestamp}_{Guid.NewGuid():N}.bak");

        try
        {
            File.Copy(_v1DatabasePath, backupPath, overwrite: false);

            if (!File.Exists(backupPath) || new FileInfo(backupPath).Length != new FileInfo(_v1DatabasePath).Length)
            {
                throw new InvalidOperationException($"Backup validation failed: file missing or size mismatch at {backupPath}");
            }

            // Verify SHA256 match
            string srcHash = ComputeFileSha256(_v1DatabasePath);
            string bakHash = ComputeFileSha256(backupPath);
            if (!string.Equals(srcHash, bakHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Backup SHA256 verification failed.");
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

        // 2. Read V1 database in strict read-only mode
        var reader = new V1ReadOnlyLibraryReader(_v1DatabasePath);
        IReadOnlyList<FolderItem> v1Folders;
        IReadOnlyList<ScoreItem> v1Scores;

        try
        {
            (v1Folders, v1Scores) = await reader.ReadAllAsync(ct);
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

        // 3. Physical file copy into V2 managed storage with compensation tracking
        var createdV2Files = new List<string>();
        var createdV2Dirs = new List<string>();
        var foldersMap = v1Folders.ToDictionary(f => f.Id);
        var migratedScores = new List<ScoreItem>();

        void EnsureDirectoryTracked(string dirPath)
        {
            var current = Path.GetFullPath(dirPath);
            var toCreate = new Stack<string>();

            while (!string.IsNullOrEmpty(current) && !Directory.Exists(current) && _fileService.IsPathUnderRoot(current))
            {
                toCreate.Push(current);
                var parent = Path.GetDirectoryName(current);
                if (parent == null || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                    break;
                current = parent;
            }

            while (toCreate.Count > 0)
            {
                var dir = toCreate.Pop();
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                    createdV2Dirs.Add(dir);
                }
            }
        }

        try
        {
            foreach (var s in v1Scores)
            {
                var v1SourcePath = s.FilePath;
                var v2Score = new ScoreItem(
                    id: s.Id,
                    title: s.Title,
                    sourceType: s.SourceType,
                    sourceUrl: !string.IsNullOrWhiteSpace(v1SourcePath) ? v1SourcePath : s.SourceUrl,
                    filePath: "",
                    originalFilename: s.OriginalFilename,
                    fileExtension: s.FileExtension,
                    folderId: s.FolderId,
                    duration: s.Duration,
                    bpm: s.Bpm,
                    totalNotes: s.TotalNotes,
                    tags: s.Tags,
                    analysisStatus: s.AnalysisStatus,
                    analysisError: s.AnalysisError,
                    favorite: s.Favorite,
                    createdAt: s.CreatedAt,
                    updatedAt: s.UpdatedAt,
                    lastPlayedAt: s.LastPlayedAt
                );

                if (!string.IsNullOrWhiteSpace(v1SourcePath) && File.Exists(v1SourcePath))
                {
                    // Check if already migrated previously (Idempotency)
                    var existingV2 = await _v2Repository.GetScoreAsync(s.Id, ct);
                    if (existingV2 != null && !string.IsNullOrEmpty(existingV2.FilePath) && File.Exists(existingV2.FilePath) && _fileService.IsPathUnderRoot(existingV2.FilePath))
                    {
                        v2Score.FilePath = existingV2.FilePath;
                        v2Score.OriginalFilename = existingV2.OriginalFilename;
                    }
                    else
                    {
                        var targetDir = _fileService.GetFolderPath(s.FolderId, foldersMap);
                        EnsureDirectoryTracked(targetDir);

                        var origName = !string.IsNullOrEmpty(s.OriginalFilename) ? s.OriginalFilename : Path.GetFileName(v1SourcePath);
                        var destFilename = _fileService.GetSafeFilename(targetDir, origName);
                        var destFilePath = Path.Combine(targetDir, destFilename);

                        File.Copy(v1SourcePath, destFilePath, overwrite: false);
                        createdV2Files.Add(destFilePath);

                        v2Score.FilePath = destFilePath;
                        v2Score.OriginalFilename = destFilename;
                    }
                }
                else
                {
                    v2Score.FilePath = "";
                    v2Score.AnalysisStatus = "MISSING_SOURCE";
                }

                migratedScores.Add(v2Score);
            }

            // 4. Atomic Bulk Import into V2 DB
            await _v2Repository.BulkImportAsync(v1Folders, migratedScores, ct);

            return new V1LibraryMigrationResult
            {
                Success = true,
                FoldersMigrated = v1Folders.Count,
                ScoresMigrated = migratedScores.Count,
                BackupPath = backupPath
            };
        }
        catch (Exception ex)
        {
            // Compensation: 1. Delete newly created V2 files
            foreach (var f in createdV2Files)
            {
                try
                {
                    if (File.Exists(f) && _fileService.IsPathUnderRoot(f))
                    {
                        File.Delete(f);
                    }
                }
                catch { }
            }

            // Compensation: 2. Delete newly created empty V2 directories deepest-first
            foreach (var d in createdV2Dirs.AsEnumerable().Reverse())
            {
                try
                {
                    if (Directory.Exists(d) && _fileService.IsPathUnderRoot(d))
                    {
                        if (!Directory.EnumerateFileSystemEntries(d).Any())
                        {
                            Directory.Delete(d, recursive: false);
                        }
                    }
                }
                catch { }
            }

            return new V1LibraryMigrationResult
            {
                Success = false,
                FoldersMigrated = 0,
                ScoresMigrated = 0,
                BackupPath = backupPath,
                ErrorMessage = $"Migration failed: {ex.Message}"
            };
        }
    }

    private static string ComputeFileSha256(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }
}
