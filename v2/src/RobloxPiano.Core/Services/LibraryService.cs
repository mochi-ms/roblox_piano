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

        // 1. Strict Format Validation & Parsing Check BEFORE Copying
        if (ext is not (".mid" or ".midi" or ".mml" or ".txt"))
        {
            throw new InvalidOperationException($"지원되지 않는 파일 형식입니다: {ext}");
        }

        double duration = 0.0;
        double bpm = 120.0;
        int totalNotes = 0;
        string status = "READY";
        string errorMsg = "";
        string title = Path.GetFileNameWithoutExtension(sourceFilePath);

        if (ext is ".mml" or ".txt")
        {
            var textContent = await File.ReadAllTextAsync(sourceFilePath, ct);
            if (string.IsNullOrWhiteSpace(textContent))
            {
                throw new InvalidOperationException("빈 파일은 악보로 등록할 수 없습니다.");
            }

            try
            {
                var meta = _mmlImporter.ExtractMetadata(textContent);
                duration = Convert.ToDouble(meta["duration"]);
                bpm = Convert.ToDouble(meta["bpm"]);
                totalNotes = Convert.ToInt32(meta["notes"]);

                if (totalNotes <= 0)
                {
                    throw new InvalidOperationException("유효한 음표 데이터가 없습니다.");
                }

                sourceType = "MML";
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"유효한 MML 악보 형식이 아니므로 등록할 수 없습니다: {ex.Message}", ex);
            }
        }
        else if (ext is ".mid" or ".midi")
        {
            try
            {
                var timeline = _midiImporter.ImportScore(sourceFilePath);
                duration = timeline.Duration;
                bpm = timeline.InitialBpm;
                totalNotes = timeline.TotalNotes;
                if (!string.IsNullOrWhiteSpace(timeline.Title) && timeline.Title != "Untitled")
                {
                    title = timeline.Title;
                }
                sourceType = "MIDI";
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"유효한 MIDI 파일이 아니므로 등록할 수 없습니다: {ex.Message}", ex);
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

    public async Task<ScoreItem> CreateScoreFromTextAsync(
        string mmlText,
        string title,
        string? folderId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(mmlText))
            throw new InvalidOperationException("빈 MML 텍스트는 악보로 등록할 수 없습니다.");

        var meta = _mmlImporter.ExtractMetadata(mmlText);
        double duration = Convert.ToDouble(meta["duration"]);
        double bpm = Convert.ToDouble(meta["bpm"]);
        int totalNotes = Convert.ToInt32(meta["notes"]);

        if (totalNotes <= 0)
        {
            throw new InvalidOperationException("유효한 음표 데이터가 없습니다.");
        }

        var allFolders = (await _repository.GetAllFoldersAsync(ct)).ToDictionary(f => f.Id);
        var targetDir = _fileService.GetFolderPath(folderId, allFolders);
        Directory.CreateDirectory(targetDir);

        var cleanTitle = string.IsNullOrWhiteSpace(title) ? "붙여넣은 MML" : _fileService.SanitizeName(title);
        var destFilename = _fileService.GetSafeFilename(targetDir, $"{cleanTitle}.mml");
        var destFilePath = Path.Combine(targetDir, destFilename);

        await File.WriteAllTextAsync(destFilePath, mmlText, ct);

        var score = new ScoreItem(
            id: Guid.NewGuid().ToString(),
            title: cleanTitle,
            sourceType: "MML",
            sourceUrl: "text://pasted-mml",
            filePath: destFilePath,
            originalFilename: destFilename,
            fileExtension: ".mml",
            folderId: folderId,
            duration: duration,
            bpm: bpm,
            totalNotes: totalNotes,
            tags: "imported,pasted",
            analysisStatus: "READY",
            analysisError: ""
        );

        try
        {
            await _repository.InsertScoreAsync(score, ct);
            return score;
        }
        catch
        {
            if (File.Exists(destFilePath) && _fileService.IsPathUnderRoot(destFilePath))
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

        if (string.IsNullOrEmpty(item.FilePath) || !_fileService.IsPathUnderRoot(item.FilePath))
        {
            throw new InvalidOperationException("관리형 스토리지 외부의 파일은 라이브러리 내부 복사를 수행할 수 없습니다.");
        }

        var allFolders = (await _repository.GetAllFoldersAsync(ct)).ToDictionary(f => f.Id);
        var targetDir = _fileService.GetFolderPath(targetFolderId, allFolders);
        Directory.CreateDirectory(targetDir);

        var filename = Path.GetFileName(item.FilePath);
        var destFilename = _fileService.GetSafeFilename(targetDir, filename);
        var destFilePath = Path.Combine(targetDir, destFilename);

        bool copiedPhysical = false;
        if (File.Exists(item.FilePath))
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
            string? tempTrash = null;
            bool movedToTemp = false;

            try
            {
                if (!string.IsNullOrEmpty(item.FilePath) && File.Exists(item.FilePath))
                {
                    if (!_fileService.IsPathUnderRoot(item.FilePath))
                    {
                        throw new InvalidOperationException("관리형 스토리지 외부의 파일은 삭제할 수 없습니다.");
                    }

                    tempTrash = $"{item.FilePath}.tmp_del_{Guid.NewGuid():N}";
                    File.Move(item.FilePath, tempTrash);
                    movedToTemp = true;
                }

                await _repository.DeleteScoreAsync(scoreId, ct);

                if (movedToTemp && !string.IsNullOrEmpty(tempTrash) && File.Exists(tempTrash))
                {
                    try { File.Delete(tempTrash); } catch { }
                }
            }
            catch
            {
                // Compensation: restore file if DB deletion failed
                if (movedToTemp && !string.IsNullOrEmpty(tempTrash) && File.Exists(tempTrash))
                {
                    try { File.Move(tempTrash, item.FilePath); } catch { }
                }
                throw;
            }
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
                    var txt = await File.ReadAllTextAsync(filePath, ct);
                    if (string.IsNullOrWhiteSpace(txt) || !_mmlImporter.CanImport(filePath))
                    {
                        skippedCount++;
                        continue;
                    }
                    var meta = _mmlImporter.ExtractMetadata(txt);
                    if (Convert.ToInt32(meta["notes"]) <= 0)
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
