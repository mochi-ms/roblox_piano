using RobloxPiano.Core.Library;

namespace RobloxPiano.Core.Services;

public class FolderService
{
    private readonly ILibraryRepository _repository;
    private readonly LibraryFileService _fileService;

    public FolderService(ILibraryRepository repository, LibraryFileService fileService)
    {
        _repository = repository;
        _fileService = fileService;
    }

    public async Task<FolderItem> CreateFolderAsync(string name, string? parentId = null, CancellationToken ct = default)
    {
        var allFolders = (await _repository.GetAllFoldersAsync(ct)).ToDictionary(f => f.Id);
        var parentPath = _fileService.GetFolderPath(parentId, allFolders);
        Directory.CreateDirectory(parentPath);

        var safeName = _fileService.SanitizeName(name);
        if (string.IsNullOrWhiteSpace(safeName) || safeName == "Untitled")
        {
            safeName = "새 폴더";
        }

        var candidatePath = Path.Combine(parentPath, safeName);
        var originalSafe = safeName;
        int counter = 1;
        while (Directory.Exists(candidatePath))
        {
            safeName = $"{originalSafe} ({counter})";
            candidatePath = Path.Combine(parentPath, safeName);
            counter++;
        }

        bool createdDir = false;
        if (!Directory.Exists(candidatePath))
        {
            Directory.CreateDirectory(candidatePath);
            createdDir = true;
        }

        var folder = new FolderItem(
            id: Guid.NewGuid().ToString(),
            parentId: parentId,
            name: safeName
        );

        try
        {
            await _repository.InsertFolderAsync(folder, ct);
            return folder;
        }
        catch
        {
            if (createdDir && Directory.Exists(candidatePath) && _fileService.IsPathUnderRoot(candidatePath))
            {
                try { Directory.Delete(candidatePath); } catch { }
            }
            throw;
        }
    }

    public async Task<FolderItem> RenameFolderAsync(string folderId, string newName, CancellationToken ct = default)
    {
        var folder = await _repository.GetFolderAsync(folderId, ct)
            ?? throw new ArgumentException($"Folder {folderId} not found");

        var allFolders = (await _repository.GetAllFoldersAsync(ct)).ToDictionary(f => f.Id);
        var oldPath = _fileService.GetFolderPath(folder.Id, allFolders);

        var cleanName = _fileService.SanitizeName(newName);
        if (string.IsNullOrWhiteSpace(cleanName) || cleanName == "Untitled")
        {
            cleanName = "새 폴더";
        }

        var parentPath = _fileService.GetFolderPath(folder.ParentId, allFolders);
        var newPath = Path.Combine(parentPath, cleanName);

        int counter = 1;
        var origCand = cleanName;
        while (Directory.Exists(newPath) && !string.Equals(Path.GetFullPath(newPath), Path.GetFullPath(oldPath), StringComparison.OrdinalIgnoreCase))
        {
            cleanName = $"{origCand} ({counter})";
            newPath = Path.Combine(parentPath, cleanName);
            counter++;
        }

        bool movedDir = false;
        if (Directory.Exists(oldPath) && !string.Equals(Path.GetFullPath(oldPath), Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase))
        {
            // Validate BOTH old and new destination paths are inside V2 managed storage root
            if (!_fileService.IsPathUnderRoot(oldPath) || !_fileService.IsPathUnderRoot(newPath))
            {
                throw new InvalidOperationException("관리형 스토리지 외부의 디렉터리는 이동할 수 없습니다.");
            }
            Directory.Move(oldPath, newPath);
            movedDir = true;
        }

        var origName = folder.Name;
        var origUpdatedAt = folder.UpdatedAt;

        try
        {
            // Discover affected scores and prepare update inside the compensation scope
            var affectedScores = await GetScoresToUpdatePathsRecursiveAsync(folderId, oldPath, newPath, ct);
            folder.Name = cleanName;
            folder.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

            await _repository.UpdateFolderAndScorePathsAsync(folder, affectedScores, ct);
            return folder;
        }
        catch
        {
            // Compensation: restore physical folder location and in-memory model
            if (movedDir && Directory.Exists(newPath) && _fileService.IsPathUnderRoot(newPath))
            {
                try { Directory.Move(newPath, oldPath); } catch { }
            }
            folder.Name = origName;
            folder.UpdatedAt = origUpdatedAt;
            throw;
        }
    }

    public bool IsDescendant(string parentFolderId, string? candidateChildId, IReadOnlyDictionary<string, FolderItem> allFolders)
    {
        if (string.IsNullOrEmpty(candidateChildId) || string.IsNullOrEmpty(parentFolderId))
            return false;

        if (parentFolderId == candidateChildId)
            return true;

        string? currentId = candidateChildId;
        var visited = new HashSet<string>();

        while (!string.IsNullOrEmpty(currentId) && visited.Add(currentId))
        {
            if (allFolders.TryGetValue(currentId, out var current))
            {
                if (current.ParentId == parentFolderId)
                    return true;
                currentId = current.ParentId;
            }
            else
            {
                break;
            }
        }

        return false;
    }

    public async Task<FolderItem> MoveFolderAsync(string folderId, string? newParentId, CancellationToken ct = default)
    {
        var folder = await _repository.GetFolderAsync(folderId, ct)
            ?? throw new ArgumentException($"Folder {folderId} not found");

        if (folder.ParentId == newParentId)
            return folder;

        var allFolders = (await _repository.GetAllFoldersAsync(ct)).ToDictionary(f => f.Id);

        // Prevent moving into itself or its own descendant (cycle prevention)
        if (folderId == newParentId || IsDescendant(folderId, newParentId, allFolders))
        {
            throw new InvalidOperationException("자기 자신이나 하위 폴더로는 이동할 수 없습니다.");
        }

        var oldPath = _fileService.GetFolderPath(folderId, allFolders);
        var newParentPath = _fileService.GetFolderPath(newParentId, allFolders);
        Directory.CreateDirectory(newParentPath);

        var safeName = folder.Name;
        var newPath = Path.Combine(newParentPath, safeName);
        int counter = 1;
        var origCand = safeName;
        while (Directory.Exists(newPath) && !string.Equals(Path.GetFullPath(newPath), Path.GetFullPath(oldPath), StringComparison.OrdinalIgnoreCase))
        {
            safeName = $"{origCand} ({counter})";
            newPath = Path.Combine(newParentPath, safeName);
            counter++;
        }

        bool movedDir = false;
        if (Directory.Exists(oldPath) && !string.Equals(Path.GetFullPath(oldPath), Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase))
        {
            // Validate BOTH old and new destination paths are inside V2 managed storage root
            if (!_fileService.IsPathUnderRoot(oldPath) || !_fileService.IsPathUnderRoot(newPath))
            {
                throw new InvalidOperationException("관리형 스토리지 외부의 디렉터리는 이동할 수 없습니다.");
            }
            Directory.Move(oldPath, newPath);
            movedDir = true;
        }

        var origName = folder.Name;
        var origParentId = folder.ParentId;
        var origUpdatedAt = folder.UpdatedAt;

        try
        {
            // Discover affected scores and prepare update inside the compensation scope
            var affectedScores = await GetScoresToUpdatePathsRecursiveAsync(folderId, oldPath, newPath, ct);
            folder.Name = safeName;
            folder.ParentId = newParentId;
            folder.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

            await _repository.UpdateFolderAndScorePathsAsync(folder, affectedScores, ct);
            return folder;
        }
        catch
        {
            // Compensation: restore physical folder location and in-memory model
            if (movedDir && Directory.Exists(newPath) && _fileService.IsPathUnderRoot(newPath))
            {
                try { Directory.Move(newPath, oldPath); } catch { }
            }
            folder.Name = origName;
            folder.ParentId = origParentId;
            folder.UpdatedAt = origUpdatedAt;
            throw;
        }
    }

    public async Task DeleteFolderAsync(string folderId, CancellationToken ct = default)
    {
        var allFolders = (await _repository.GetAllFoldersAsync(ct)).ToDictionary(f => f.Id);
        if (!allFolders.TryGetValue(folderId, out var targetFolder))
        {
            return;
        }

        // 1. Preflight: Discover entire folder subtree (deepest-first for clean folder deletion order)
        var foldersToDelete = new List<FolderItem>();
        void CollectFoldersDeepestFirst(string currentId)
        {
            var children = allFolders.Values.Where(f => f.ParentId == currentId).ToList();
            foreach (var child in children)
            {
                CollectFoldersDeepestFirst(child.Id);
            }
            if (allFolders.TryGetValue(currentId, out var item))
            {
                foldersToDelete.Add(item);
            }
        }
        CollectFoldersDeepestFirst(folderId);

        // 2. Preflight: Discover all scores in the entire subtree
        var scoresToDelete = new List<ScoreItem>();
        foreach (var folder in foldersToDelete)
        {
            var page = await _repository.QueryScoresAsync(new LibraryQuery { FolderId = folder.Id, PageSize = 10000 }, ct);
            scoresToDelete.AddRange(page.Items);
        }

        // 3. Preflight: Validate paths and ownership
        var rootPhysicalPath = _fileService.GetFolderPath(folderId, allFolders);

        // Storage root guard
        if (string.Equals(Path.GetFullPath(rootPhysicalPath), Path.GetFullPath(_fileService.StorageRoot), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("라이브러리 루트 디렉터리는 삭제할 수 없습니다.");
        }

        if (!_fileService.IsPathUnderRoot(rootPhysicalPath))
        {
            throw new InvalidOperationException("관리형 스토리지 외부의 디렉터리는 삭제할 수 없습니다.");
        }

        // Validate all existing physical files in the subtree
        foreach (var s in scoresToDelete)
        {
            if (!string.IsNullOrEmpty(s.FilePath) && File.Exists(s.FilePath))
            {
                if (!_fileService.IsPathUnderRoot(s.FilePath))
                {
                    throw new InvalidOperationException($"관리형 스토리지 외부의 악보 파일이 포함되어 있어 삭제를 중단합니다: {s.FilePath}");
                }

                var rel = Path.GetRelativePath(rootPhysicalPath, s.FilePath);
                if (rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel))
                {
                    throw new InvalidOperationException($"대상 폴더 트리 외부의 악보 파일이 연결되어 있어 삭제를 중단합니다: {s.FilePath}");
                }
            }
        }

        // 4. Physical Staging (Move rootPhysicalPath to temporary staging folder inside V2 root)
        bool staged = false;
        string? stagedPath = null;
        string? stagingBase = null;

        if (Directory.Exists(rootPhysicalPath))
        {
            stagingBase = Path.Combine(_fileService.StorageRoot, ".delete-staging");
            Directory.CreateDirectory(stagingBase);
            stagedPath = Path.Combine(stagingBase, $"{Guid.NewGuid():N}");
            Directory.Move(rootPhysicalPath, stagedPath);
            staged = true;
        }

        // 5. Transactional DB Subtree Delete
        try
        {
            var scoreIds = scoresToDelete.Select(s => s.Id).ToList();
            var folderIds = foldersToDelete.Select(f => f.Id).ToList();

            await _repository.DeleteFolderTreeAsync(scoreIds, folderIds, ct);
        }
        catch
        {
            // Compensation on DB failure / cancellation: restore physical subtree from staging
            if (staged && !string.IsNullOrEmpty(stagedPath) && Directory.Exists(stagedPath) && _fileService.IsPathUnderRoot(stagedPath))
            {
                try { Directory.Move(stagedPath, rootPhysicalPath); } catch { }
                try
                {
                    if (stagingBase != null && Directory.Exists(stagingBase) && !Directory.EnumerateFileSystemEntries(stagingBase).Any())
                    {
                        Directory.Delete(stagingBase);
                    }
                }
                catch { }
            }
            throw;
        }

        // 6. Finalize Staged Cleanup (After successful DB commit)
        if (staged && !string.IsNullOrEmpty(stagedPath) && Directory.Exists(stagedPath))
        {
            try
            {
                Directory.Delete(stagedPath, recursive: true);
            }
            catch
            {
                // Final cleanup policy: If locked by external process/antivirus, leave safely quarantined under .delete-staging
            }

            try
            {
                if (stagingBase != null && Directory.Exists(stagingBase) && !Directory.EnumerateFileSystemEntries(stagingBase).Any())
                {
                    Directory.Delete(stagingBase);
                }
            }
            catch { }
        }
    }

    public async Task<int> CleanSpuriousEmptyFoldersAsync(CancellationToken ct = default)
    {
        var allFolders = (await _repository.GetAllFoldersAsync(ct)).ToDictionary(f => f.Id);
        int cleanedCount = 0;

        foreach (var (id, f) in allFolders)
        {
            int scoreCount = await _repository.GetScoreCountAsync(f.Id, ct: ct);
            var children = await _repository.GetChildFoldersAsync(f.Id, ct);
            var physicalPath = _fileService.GetFolderPath(f.Id, allFolders);

            if (scoreCount == 0 && children.Count == 0 && !Directory.Exists(physicalPath))
            {
                await _repository.DeleteFolderAsync(f.Id, ct);
                cleanedCount++;
            }
        }

        return cleanedCount;
    }

    private async Task<List<ScoreItem>> GetScoresToUpdatePathsRecursiveAsync(string folderId, string oldBasePath, string newBasePath, CancellationToken ct)
    {
        var list = new List<ScoreItem>();
        var page = await _repository.QueryScoresAsync(new LibraryQuery { FolderId = folderId, PageSize = 10000 }, ct);
        foreach (var s in page.Items)
        {
            if (!string.IsNullOrEmpty(s.FilePath))
            {
                var rel = Path.GetRelativePath(oldBasePath, s.FilePath);
                // Robust descendant check (not starting with .. and not absolute)
                if (!rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel))
                {
                    s.FilePath = Path.Combine(newBasePath, rel);
                    s.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
                    list.Add(s);
                }
            }
        }

        var children = await _repository.GetChildFoldersAsync(folderId, ct);
        foreach (var child in children)
        {
            var childList = await GetScoresToUpdatePathsRecursiveAsync(child.Id, oldBasePath, newBasePath, ct);
            list.AddRange(childList);
        }

        return list;
    }
}
