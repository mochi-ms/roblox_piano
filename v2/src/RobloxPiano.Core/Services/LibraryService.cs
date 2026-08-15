using System.Text.RegularExpressions;
using RobloxPiano.Core.Importers;
using RobloxPiano.Core.Library;

namespace RobloxPiano.Core.Services;

public class LibraryService
{
    private readonly ILibraryRepository _repository;
    private readonly LibraryFileService _fileService;
    private readonly FolderService _folderService;
    private readonly MidiImporter _midiImporter = new();
    private readonly MmlImporter _mmlImporter = new();

    public LibraryService(
        ILibraryRepository repository,
        LibraryFileService fileService,
        FolderService folderService)
    {
        _repository = repository;
        _fileService = fileService;
        _folderService = folderService;
    }

    public ILibraryRepository Repository => _repository;
    public LibraryFileService FileService => _fileService;
    public FolderService FolderService => _folderService;

    public async Task<ScoreItem> ImportExternalFileAsync(
        string sourceFilePath,
        string? folderId = null,
        string sourceType = "FILE",
        CancellationToken ct = default)
    {
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException($"Source score file not found: {sourceFilePath}", sourceFilePath);

        var ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        var allFolders = (await _repository.GetAllFoldersAsync(ct)).ToDictionary(f => f.Id);
        var targetDir = _fileService.GetFolderPath(folderId, allFolders);
        Directory.CreateDirectory(targetDir);

        var originalFilename = Path.GetFileName(sourceFilePath);
        string destFilename;
        string destFilePath;

        if (string.Equals(Path.GetFullPath(Path.GetDirectoryName(sourceFilePath) ?? ""), Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase))
        {
            destFilename = originalFilename;
            destFilePath = sourceFilePath;
        }
        else
        {
            destFilename = _fileService.GetSafeFilename(targetDir, originalFilename);
            destFilePath = Path.Combine(targetDir, destFilename);
            File.Copy(sourceFilePath, destFilePath, overwrite: false);
        }

        double duration = 0.0;
        double bpm = 120.0;
        int totalNotes = 0;
        string status = "READY";
        string errorMsg = "";
        string title = Path.GetFileNameWithoutExtension(destFilename);

        try
        {
            if (ext is ".mid" or ".midi")
            {
                var timeline = _midiImporter.ImportScore(destFilePath);
                duration = timeline.Duration;
                bpm = timeline.InitialBpm;
                totalNotes = timeline.TotalNotes;
                if (!string.IsNullOrWhiteSpace(timeline.Title) && timeline.Title != "Untitled")
                {
                    title = timeline.Title;
                }
                sourceType = "MIDI";
            }
            else if (ext is ".mml" or ".txt")
            {
                if (_mmlImporter.CanImport(destFilePath))
                {
                    var mmlText = await File.ReadAllTextAsync(destFilePath, ct);
                    var meta = _mmlImporter.ExtractMetadata(mmlText);
                    duration = Convert.ToDouble(meta["duration"]);
                    bpm = Convert.ToDouble(meta["bpm"]);
                    totalNotes = Convert.ToInt32(meta["notes"]);
                    sourceType = "MML";
                }
            }
        }
        catch (Exception ex)
        {
            status = "READY";
            errorMsg = ex.Message;
        }

        var score = new ScoreItem(
            id: Guid.NewGuid().ToString(),
            title: title,
            sourceType: sourceType,
            sourceUrl: sourceFilePath,
            filePath: destFilePath,
            originalFilename: destFilename,
            fileExtension: ext,
            folderId: folderId,
            duration: duration,
            bpm: bpm,
            totalNotes: totalNotes,
            tags: "imported",
            analysisStatus: status,
            analysisError: errorMsg
        );

        await _repository.InsertScoreAsync(score, ct);
        return score;
    }

    public async Task<ScoreItem> RenameScoreAsync(string scoreId, string newTitle, CancellationToken ct = default)
    {
        var item = await _repository.GetScoreAsync(scoreId, ct)
            ?? throw new ArgumentException($"Score {scoreId} not found");

        var oldFilePath = item.FilePath;
        var targetDir = Path.GetDirectoryName(oldFilePath) ?? _fileService.StorageRoot;

        var cleanName = _fileService.SanitizeName(newTitle);
        var ext = Path.GetExtension(cleanName);
        if (string.IsNullOrEmpty(ext) && !string.IsNullOrEmpty(item.FileExtension))
        {
            cleanName = $"{cleanName}{item.FileExtension}";
        }

        var destFilename = _fileService.GetSafeFilename(targetDir, cleanName, ignoreFilePath: oldFilePath);
        var newFilePath = Path.Combine(targetDir, destFilename);

        if (File.Exists(oldFilePath) && !string.Equals(Path.GetFullPath(oldFilePath), Path.GetFullPath(newFilePath), StringComparison.OrdinalIgnoreCase))
        {
            File.Move(oldFilePath, newFilePath);
        }

        item.Title = Path.GetFileNameWithoutExtension(destFilename);
        item.FilePath = newFilePath;
        item.OriginalFilename = destFilename;
        item.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

        await _repository.UpdateScoreAsync(item, ct);
        return item;
    }

    public async Task<ScoreItem> CopyScoreAsync(string scoreId, string? targetFolderId, CancellationToken ct = default)
    {
        var item = await _repository.GetScoreAsync(scoreId, ct)
            ?? throw new ArgumentException($"Score {scoreId} not found");

        var allFolders = (await _repository.GetAllFoldersAsync(ct)).ToDictionary(f => f.Id);
        var targetDir = _fileService.GetFolderPath(targetFolderId, allFolders);
        Directory.CreateDirectory(targetDir);

        var filename = Path.GetFileName(item.FilePath);
        var destFilename = _fileService.GetSafeFilename(targetDir, filename);
        var destFilePath = Path.Combine(targetDir, destFilename);

        if (File.Exists(item.FilePath))
        {
            File.Copy(item.FilePath, destFilePath);
        }

        var newItem = new ScoreItem(
            id: Guid.NewGuid().ToString(),
            title: Path.GetFileNameWithoutExtension(destFilename),
            sourceType: item.SourceType,
            sourceUrl: item.SourceUrl,
            filePath: destFilePath,
            originalFilename: destFilename,
            fileExtension: item.FileExtension,
            folderId: targetFolderId,
            duration: item.Duration,
            bpm: item.Bpm,
            totalNotes: item.TotalNotes,
            tags: item.Tags,
            analysisStatus: item.AnalysisStatus
        );

        await _repository.InsertScoreAsync(newItem, ct);
        return newItem;
    }

    public async Task MoveScoreAsync(string scoreId, string? targetFolderId, CancellationToken ct = default)
    {
        var item = await _repository.GetScoreAsync(scoreId, ct)
            ?? throw new ArgumentException($"Score {scoreId} not found");

        if (item.FolderId == targetFolderId)
            return;

        var allFolders = (await _repository.GetAllFoldersAsync(ct)).ToDictionary(f => f.Id);
        var targetDir = _fileService.GetFolderPath(targetFolderId, allFolders);
        Directory.CreateDirectory(targetDir);

        var oldPath = item.FilePath;
        if (File.Exists(oldPath))
        {
            var filename = Path.GetFileName(oldPath);
            var destFilename = _fileService.GetSafeFilename(targetDir, filename);
            var newPath = Path.Combine(targetDir, destFilename);

            File.Move(oldPath, newPath);
            item.FilePath = newPath;
            item.OriginalFilename = destFilename;
        }

        item.FolderId = targetFolderId;
        item.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        await _repository.UpdateScoreAsync(item, ct);
    }

    public async Task DeleteScoreAsync(string scoreId, CancellationToken ct = default)
    {
        var item = await _repository.GetScoreAsync(scoreId, ct);
        if (item != null)
        {
            if (File.Exists(item.FilePath) && _fileService.IsPathUnderRoot(item.FilePath))
            {
                File.Delete(item.FilePath);
            }
            await _repository.DeleteScoreAsync(scoreId, ct);
        }
    }

    public async Task<LibraryImportSummary> ImportFolderRecursiveAsync(
        string sourceFolderPath,
        string? targetParentFolderId = null,
        Action<int, int, string>? progressCallback = null,
        Func<bool>? cancelCheck = null,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(sourceFolderPath))
            throw new DirectoryNotFoundException($"Source folder not found: {sourceFolderPath}");

        var rootName = Path.GetFileName(Path.GetFullPath(sourceFolderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(rootName)) rootName = "악보";

        var existingFolders = await _repository.GetAllFoldersAsync(ct);
        var rootFolder = existingFolders.FirstOrDefault(f => f.ParentId == targetParentFolderId && string.Equals(f.Name, rootName, StringComparison.OrdinalIgnoreCase));

        int importedFoldersCount = 0;
        if (rootFolder == null)
        {
            rootFolder = await _folderService.CreateFolderAsync(rootName, targetParentFolderId, ct);
            importedFoldersCount++;
        }

        var allFilesToProcess = Directory.GetFiles(sourceFolderPath, "*.*", SearchOption.AllDirectories);
        int totalFiles = allFilesToProcess.Length;

        var relDirToFolderId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [""] = rootFolder.Id
        };

        int importedScoresCount = 0;
        int skippedCount = 0;
        int failedCount = 0;
        var failedItems = new List<(string Path, string Reason)>();
        int processedCount = 0;
        bool isCancelled = false;

        var ignoreExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".ini", ".db", ".ds_store", ".tmp", ".bak", ".log" };
        var supportedExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mid", ".midi", ".mml", ".txt" };

        var allDirs = Directory.GetDirectories(sourceFolderPath, "*", SearchOption.AllDirectories);
        foreach (var dir in allDirs)
        {
            if (cancelCheck?.Invoke() == true || ct.IsCancellationRequested)
            {
                isCancelled = true;
                break;
            }

            var relDir = Path.GetRelativePath(sourceFolderPath, dir);
            var parentRelDir = Path.GetDirectoryName(relDir) ?? "";
            var dirName = Path.GetFileName(dir);

            string curParentFolderId = relDirToFolderId.TryGetValue(parentRelDir, out var pId) ? pId : rootFolder.Id;

            existingFolders = await _repository.GetAllFoldersAsync(ct);
            var existingSub = existingFolders.FirstOrDefault(f => f.ParentId == curParentFolderId && string.Equals(f.Name, dirName, StringComparison.OrdinalIgnoreCase));

            if (existingSub == null)
            {
                existingSub = await _folderService.CreateFolderAsync(dirName, curParentFolderId, ct);
                importedFoldersCount++;
            }

            relDirToFolderId[relDir] = existingSub.Id;
        }

        foreach (var filePath in allFilesToProcess)
        {
            if (cancelCheck?.Invoke() == true || ct.IsCancellationRequested)
            {
                isCancelled = true;
                break;
            }

            processedCount++;
            var fname = Path.GetFileName(filePath);
            var lowerFname = fname.ToLowerInvariant();
            var ext = Path.GetExtension(lowerFname);

            if (lowerFname is "desktop.ini" or "thumbs.db" or ".ds_store" || lowerFname.StartsWith("readme") || lowerFname.StartsWith("license"))
            {
                skippedCount++;
                continue;
            }

            if (ignoreExts.Contains(ext) || !supportedExts.Contains(ext))
            {
                skippedCount++;
                continue;
            }

            if (ext == ".txt")
            {
                try
                {
                    using var reader = new StreamReader(filePath);
                    char[] buf = new char[100];
                    int r = reader.Read(buf, 0, 100);
                    var snippet = new string(buf, 0, r).Trim().ToUpperInvariant();
                    if (!snippet.StartsWith("MML@"))
                    {
                        skippedCount++;
                        continue;
                    }
                }
                catch
                {
                    skippedCount++;
                    continue;
                }
            }

            var fileDir = Path.GetDirectoryName(filePath) ?? sourceFolderPath;
            var relDir = Path.GetRelativePath(sourceFolderPath, fileDir);
            if (relDir == ".") relDir = "";

            string folderId = relDirToFolderId.TryGetValue(relDir, out var fId) ? fId : rootFolder.Id;

            try
            {
                await ImportExternalFileAsync(filePath, folderId, ct: ct);
                importedScoresCount++;
            }
            catch (Exception ex)
            {
                failedCount++;
                failedItems.Add((filePath, ex.Message));
            }

            progressCallback?.Invoke(processedCount, totalFiles, fname);
        }

        return new LibraryImportSummary
        {
            RootFolderId = rootFolder.Id,
            RootFolderName = rootFolder.Name,
            TotalScanned = totalFiles,
            ImportedFolders = importedFoldersCount,
            ImportedScores = importedScoresCount,
            Skipped = skippedCount,
            Failed = failedCount,
            FailedItems = failedItems,
            Cancelled = isCancelled
        };
    }
}
