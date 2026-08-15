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

        Directory.CreateDirectory(candidatePath);

        var folder = new FolderItem(
            id: Guid.NewGuid().ToString(),
            parentId: parentId,
            name: safeName
        );

        await _repository.InsertFolderAsync(folder, ct);
        return folder;
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

        if (Directory.Exists(oldPath) && !string.Equals(Path.GetFullPath(oldPath), Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase))
        {
            Directory.Move(oldPath, newPath);
            await UpdateScorePathsRecursiveAsync(folderId, oldPath, newPath, ct);
        }

        folder.Name = cleanName;
        folder.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        await _repository.UpdateFolderAsync(folder, ct);

        return folder;
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

        if (Directory.Exists(oldPath) && !string.Equals(Path.GetFullPath(oldPath), Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase))
        {
            Directory.Move(oldPath, newPath);
            await UpdateScorePathsRecursiveAsync(folderId, oldPath, newPath, ct);
        }

        folder.Name = safeName;
        folder.ParentId = newParentId;
        folder.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        await _repository.UpdateFolderAsync(folder, ct);

        return folder;
    }

    public async Task DeleteFolderAsync(string folderId, CancellationToken ct = default)
    {
        var allFolders = (await _repository.GetAllFoldersAsync(ct)).ToDictionary(f => f.Id);
        var childFolders = await _repository.GetChildFoldersAsync(folderId, ct);
        foreach (var child in childFolders)
        {
            await DeleteFolderAsync(child.Id, ct);
        }

        var page = await _repository.QueryScoresAsync(new LibraryQuery { FolderId = folderId, PageSize = 10000 }, ct);
        foreach (var score in page.Items)
        {
            if (File.Exists(score.FilePath) && _fileService.IsPathUnderRoot(score.FilePath))
            {
                File.Delete(score.FilePath);
            }
            await _repository.DeleteScoreAsync(score.Id, ct);
        }

        var physicalPath = _fileService.GetFolderPath(folderId, allFolders);
        if (Directory.Exists(physicalPath) && _fileService.IsPathUnderRoot(physicalPath))
        {
            Directory.Delete(physicalPath, recursive: true);
        }

        await _repository.DeleteFolderAsync(folderId, ct);
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

    private async Task UpdateScorePathsRecursiveAsync(string folderId, string oldBasePath, string newBasePath, CancellationToken ct)
    {
        var page = await _repository.QueryScoresAsync(new LibraryQuery { FolderId = folderId, PageSize = 10000 }, ct);
        foreach (var s in page.Items)
        {
            if (s.FilePath.StartsWith(oldBasePath, StringComparison.OrdinalIgnoreCase))
            {
                var relPath = Path.GetRelativePath(oldBasePath, s.FilePath);
                s.FilePath = Path.Combine(newBasePath, relPath);
                await _repository.UpdateScoreAsync(s, ct);
            }
        }

        var children = await _repository.GetChildFoldersAsync(folderId, ct);
        foreach (var child in children)
        {
            await UpdateScorePathsRecursiveAsync(child.Id, oldBasePath, newBasePath, ct);
        }
    }
}
