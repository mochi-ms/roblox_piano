using System.IO;
using RobloxPiano.Infrastructure.Data;

namespace RobloxPiano.Infrastructure.Transcription;

public class TranscriptionWorkspaceService
{
    public string WorkspaceRoot { get; }

    public TranscriptionWorkspaceService(string? workspaceRoot = null)
    {
        WorkspaceRoot = !string.IsNullOrWhiteSpace(workspaceRoot)
            ? Path.GetFullPath(workspaceRoot)
            : Path.GetFullPath(LibraryDatabasePathProvider.GetDefaultTranscriptionWorkspaceRoot());
    }

    public static bool IsValidJobId(string? jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId) || jobId.Length > 128)
        {
            return false;
        }

        // Strict whitelist: letters, digits, underscores, hyphens only
        foreach (char c in jobId)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-')
            {
                return false;
            }
        }

        return true;
    }

    public string GetSafeJobDirectoryPath(string jobId, bool createDirectory = false)
    {
        if (!IsValidJobId(jobId))
        {
            throw new ArgumentException($"유효하지 않거나 안전하지 않은 작업 ID입니다: '{jobId}'", nameof(jobId));
        }

        string fullRoot = Path.GetFullPath(WorkspaceRoot);
        string combined = Path.GetFullPath(Path.Combine(fullRoot, jobId));

        // Defense-in-depth: verify separator-aware containment
        string normalizedRoot = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string normalizedCombined = combined.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!normalizedCombined.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedRoot, normalizedCombined, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"작업 경로가 워크스페이스 루트를 벗어났습니다: '{jobId}'");
        }

        if (createDirectory && !Directory.Exists(combined))
        {
            Directory.CreateDirectory(combined);
        }

        return combined;
    }

    public string GetJobDirectory(string jobId, bool createDirectory = true)
    {
        return GetSafeJobDirectoryPath(jobId, createDirectory);
    }

    public string GetTempMidiPath(string jobId, bool createDirectory = true)
    {
        string jobDir = GetSafeJobDirectoryPath(jobId, createDirectory);
        return Path.Combine(jobDir, "transcription.tmp.mid");
    }

    public string GetFinalMidiPath(string jobId, bool createDirectory = true)
    {
        string jobDir = GetSafeJobDirectoryPath(jobId, createDirectory);
        return Path.Combine(jobDir, "transcription.mid");
    }

    public string CommitMidiFile(string jobId)
    {
        string tempPath = GetTempMidiPath(jobId);
        string finalPath = GetFinalMidiPath(jobId);

        if (!File.Exists(tempPath))
        {
            throw new FileNotFoundException($"임시 MIDI 파일이 존재하지 않습니다: {tempPath}", tempPath);
        }

        if (File.Exists(finalPath))
        {
            File.Delete(finalPath);
        }

        File.Move(tempPath, finalPath);
        return finalPath;
    }

    public void CleanJob(string? jobId)
    {
        if (!IsValidJobId(jobId))
        {
            return; // Safe no-op for invalid/malformed ID to prevent unintended deletion
        }

        try
        {
            string fullRoot = Path.GetFullPath(WorkspaceRoot);
            string combined = Path.GetFullPath(Path.Combine(fullRoot, jobId!));

            string normalizedRoot = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string normalizedCombined = combined.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            // Safety guard: never delete outside root or the root directory itself
            if (!normalizedCombined.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedRoot, normalizedCombined, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (Directory.Exists(combined))
            {
                Directory.Delete(combined, recursive: true);
            }
        }
        catch
        {
            // Ignore workspace cleanup failures
        }
    }
}
