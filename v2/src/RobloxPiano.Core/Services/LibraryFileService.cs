using System.Text.RegularExpressions;
using RobloxPiano.Core.Library;

namespace RobloxPiano.Core.Services;

public class LibraryFileService
{
    private readonly string _storageRoot;

    public LibraryFileService(string storageRoot)
    {
        _storageRoot = Path.GetFullPath(storageRoot);
        if (!Directory.Exists(_storageRoot))
        {
            Directory.CreateDirectory(_storageRoot);
        }
    }

    public string StorageRoot => _storageRoot;

    public string SanitizeName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return "Untitled";

        var clean = Regex.Replace(rawName, @"[\\/*?:""<>|]", "").Trim();
        clean = clean.Trim('.', ' ');

        return string.IsNullOrWhiteSpace(clean) ? "Untitled" : clean;
    }

    public bool IsPathUnderRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(_storageRoot, StringComparison.OrdinalIgnoreCase);
    }

    public string GetFolderPath(string? folderId, IReadOnlyDictionary<string, FolderItem> allFolders)
    {
        if (string.IsNullOrEmpty(folderId))
            return _storageRoot;

        var pathParts = new List<string>();
        string? currentId = folderId;
        var visited = new HashSet<string>();

        while (!string.IsNullOrEmpty(currentId) && visited.Add(currentId))
        {
            if (allFolders.TryGetValue(currentId, out var folder))
            {
                pathParts.Add(SanitizeName(folder.Name));
                currentId = folder.ParentId;
            }
            else
            {
                break;
            }
        }

        pathParts.Reverse();
        return Path.Combine(_storageRoot, Path.Combine(pathParts.ToArray()));
    }

    public string GetSafeFilename(string targetDir, string desiredName, string? ignoreFilePath = null)
    {
        var cleanName = SanitizeName(desiredName);
        var baseName = Path.GetFileNameWithoutExtension(cleanName);
        var ext = Path.GetExtension(cleanName);

        var candidate = cleanName;
        int counter = 1;

        while (true)
        {
            var fullCand = Path.Combine(targetDir, candidate);
            if (!File.Exists(fullCand))
                break;

            if (!string.IsNullOrEmpty(ignoreFilePath) &&
                string.Equals(Path.GetFullPath(fullCand), Path.GetFullPath(ignoreFilePath), StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            candidate = $"{baseName} ({counter}){ext}";
            counter++;
        }

        return candidate;
    }
}
