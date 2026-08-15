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

        // 1. Format Validation BEFORE Copying
        if (ext is not (".mid" or ".midi" or ".mml" or ".txt"))
        {
            throw new InvalidOperationException($"지원되지 않는 파일 형식입니다: {ext}");
        }

        if (ext == ".txt")
        {
            if (!_mmlImporter.CanImport(sourceFilePath))
            {
                throw new InvalidOperationException("지원되지 않는 형식이거나 유효하지 않은 텍스트 악보 파일입니다.");
            }

            var textContent = await File.ReadAllTextAsync(sourceFilePath, ct);
            var headSnippet = textContent.Trim().ToUpperInvariant();
            if (!headSnippet.StartsWith("MML@") && !Regex.IsMatch(headSnippet, @"\b[1-7][+#-]?\b"))
            {
                throw new InvalidOperationException("일반 텍스트 파일(README 등)은 악보로 등록할 수 없습니다.");
            }
        }
        else if (ext == ".mml")
        {
            if (!_mmlImporter.CanImport(sourceFilePath))
            {
                throw new InvalidOperationException("유효한 MML 형식이 아닙니다.");
            }
        }

        // 2. Prepare Destination inside Managed V2 Storage
        var allFolders = (await _repository.GetAllFoldersAsync(ct)).ToDictionary(f => f.Id);
        var targetDir = _fileService.GetFolderPath(folderId, allFolders);
        Directory.CreateDirectory(targetDir);

        var originalFilename = Path.GetFileName(sourceFilePath);
        string destFilename;
        string destFilePath;
        bool copiedFile = false;

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
            copiedFile = true;
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
                var mmlText = await File.ReadAllTextAsync(destFilePath, ct);
                var meta = _mmlImporter.ExtractMetadata(mmlText);
                duration = Convert.ToDouble(meta["duration"]);
                bpm = Convert.ToDouble(meta["bpm"]);
                totalNotes = Convert.ToInt32(meta["notes"]);
                sourceType = "MML";
            }
        }
        catch (Exception ex)
        {
            status = "ANALYSIS_FAILED";
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

        try
        {
            await _repository.InsertScoreAsync(score, ct);
            return score;
        }
        catch
        {
            // Compensation: delete copied destination file if DB insertion failed
            if (copiedFile && File.Exists(destFilePath) && _fileService.IsPathUnderRoot(destFilePath))
            {
                try { File.Delete(destFilePath); } catch { }
            }
            throw;
        }
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

        bool movedPhysical = false;
        if (!string.IsNullOrEmpty(oldFilePath) && File.Exists(oldFilePath) && !string.Equals(Path.GetFullPath(oldFilePath), Path.GetFullPath(newFilePath), StringComparison.OrdinalIgnoreCase))
        {
            if (!_fileService.IsPathUnderRoot(oldFilePath))
            {
                throw new InvalidOperationException("관리형 스토리지 외부의 파일은 직접 이름을 바꿀 수 없습니다.");
            }
            File.Move(oldFilePath, newFilePath);
            movedPhysical = true;
        }

        var originalTitle = item.Title;
        var originalFilePath = item.FilePath;
        var originalFilename = item.OriginalFilename;

        item.Title = Path.GetFileNameWithoutExtension(destFilename);
        item.FilePath = newFilePath;
        item.OriginalFilename = destFilename;
        item.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

        try
        {
            await _repository.UpdateScoreAsync(item, ct);
            return item;
        }
        catch
        {
            // Compensation: restore file name and in-memory model
            if (movedPhysical && File.Exists(newFilePath) && _fileService.IsPathUnderRoot(newFilePath))
            {
                try { File.Move(newFilePath, oldFilePath); } catch { }
            }
            item.Title = originalTitle;
            item.FilePath = originalFilePath;
            item.OriginalFilename = originalFilename;
            throw;
        }
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

        bool copiedPhysical = false;
        if (!string.IsNullOrEmpty(item.FilePath) && File.Exists(item.FilePath))
        {
            File.Copy(item.FilePath, destFilePath);
            copiedPhysical = true;
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
            analysisStatus: item.AnalysisStatus,
            analysisError: item.AnalysisError
        );

        try
        {
            await _repository.InsertScoreAsync(newItem, ct);
            return newItem;
        }
        catch
        {
            // Compensation: delete copied file
            if (copiedPhysical && File.Exists(destFilePath) && _fileService.IsPathUnderRoot(destFilePath))
            {
                try { File.Delete(destFilePath); } catch { }
            }
            throw;
        }
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
        string? newPath = null;
        string? destFilename = null;
        bool movedPhysical = false;

        if (!string.IsNullOrEmpty(oldPath) && File.Exists(oldPath))
        {
            if (!_fileService.IsPathUnderRoot(oldPath))
            {
                throw new InvalidOperationException("관리형 스토리지 외부의 파일은 이동할 수 없습니다.");
            }

            var filename = Path.GetFileName(oldPath);
            destFilename = _fileService.GetSafeFilename(targetDir, filename);
            newPath = Path.Combine(targetDir, destFilename);

            File.Move(oldPath, newPath);
            movedPhysical = true;
            item.FilePath = newPath;
            item.OriginalFilename = destFilename;
        }

        var oldFolderId = item.FolderId;
        item.FolderId = targetFolderId;
        item.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

        try
        {
            await _repository.UpdateScoreAsync(item, ct);
        }
        catch
        {
            // Compensation: move file back
            if (movedPhysical && !string.IsNullOrEmpty(newPath) && File.Exists(newPath) && _fileService.IsPathUnderRoot(newPath))
            {
                try { File.Move(newPath, oldPath); } catch { }
            }
            item.FilePath = oldPath;
            item.FolderId = oldFolderId;
            throw;
        }
    }

    public async Task DeleteScoreAsync(string scoreId, CancellationToken ct = default)
    {
        var item = await _repository.GetScoreAsync(scoreId, ct);
        if (item != null)
        {
            if (!string.IsNullOrEmpty(item.FilePath) && File.Exists(item.FilePath))
            {
                if (_fileService.IsPathUnderRoot(item.FilePath))
                {
                    File.Delete(item.FilePath);
                }
                else
                {
                    throw new InvalidOperationException("관리형 스토리지 외부의 파일은 삭제할 수 없습니다.");
                }
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
