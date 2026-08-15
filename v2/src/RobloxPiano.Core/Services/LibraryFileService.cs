using System.Text.RegularExpressions;
using RobloxPiano.Core.Library;

namespace RobloxPiano.Core.Services;

public class LibraryFileService
{
    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

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

        if (string.IsNullOrWhiteSpace(clean) || clean == "." || clean == "..")
            return "Untitled";

        var baseName = Path.GetFileNameWithoutExtension(clean);

        if (ReservedWindowsNames.Contains(baseName) || ReservedWindowsNames.Contains(clean))
        {
            clean = $"_{clean}";
        }

        return clean;
    }

    public bool IsPathUnderRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetFullPath(_storageRoot);

            if (string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
                return true;

            var rel = Path.GetRelativePath(root, fullPath);
            return rel != ".." 
                && !rel.StartsWith(".." + Path.DirectorySeparatorChar) 
                && !rel.StartsWith(".." + Path.AltDirectorySeparatorChar) 
                && !Path.IsPathRooted(rel);
        }
        catch
        {
            return false;
        }
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
