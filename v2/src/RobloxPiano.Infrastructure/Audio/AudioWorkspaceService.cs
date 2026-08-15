using System.IO;
using RobloxPiano.Infrastructure.Data;

namespace RobloxPiano.Infrastructure.Audio;

public class AudioWorkspaceService
{
    public string WorkspaceRoot { get; }

    public AudioWorkspaceService(string? workspaceRoot = null)
    {
        WorkspaceRoot = !string.IsNullOrWhiteSpace(workspaceRoot)
            ? workspaceRoot
            : LibraryDatabasePathProvider.GetDefaultAudioWorkspaceRoot();
    }

    public string GetJobDirectory(string jobId)
    {
        string jobDir = Path.Combine(WorkspaceRoot, jobId);
        if (!Directory.Exists(jobDir))
        {
            Directory.CreateDirectory(jobDir);
        }
        return jobDir;
    }

    public string GetTempNormalizedPath(string jobId)
    {
        string jobDir = GetJobDirectory(jobId);
        return Path.Combine(jobDir, "normalized.tmp.wav");
    }

    public string GetFinalNormalizedPath(string jobId)
    {
        string jobDir = GetJobDirectory(jobId);
        return Path.Combine(jobDir, "normalized.wav");
    }

    public string CommitNormalizedFile(string jobId)
    {
        string tempPath = GetTempNormalizedPath(jobId);
        string finalPath = GetFinalNormalizedPath(jobId);

        if (!File.Exists(tempPath))
        {
            throw new FileNotFoundException($"임시 변환 파일이 존재하지 않습니다: {tempPath}", tempPath);
        }

        if (File.Exists(finalPath))
        {
            File.Delete(finalPath);
        }

        File.Move(tempPath, finalPath);
        return finalPath;
    }

    public void CleanJob(string jobId)
    {
        try
        {
            string jobDir = Path.Combine(WorkspaceRoot, jobId);
            if (Directory.Exists(jobDir))
            {
                Directory.Delete(jobDir, true);
            }
        }
        catch
        {
            // Ignore workspace cleanup failures
        }
    }
}
