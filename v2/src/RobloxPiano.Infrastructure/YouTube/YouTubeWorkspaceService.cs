using System.Text.RegularExpressions;

namespace RobloxPiano.Infrastructure.YouTube;

public class YouTubeWorkspaceService
{
    private static readonly Regex JobIdRegex = new(@"^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);
    private readonly string _workspaceRoot;

    public string WorkspaceRoot => _workspaceRoot;

    public YouTubeWorkspaceService(string? customRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(customRoot))
        {
            _workspaceRoot = Path.GetFullPath(customRoot);
        }
        else
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _workspaceRoot = Path.Combine(localAppData, "RobloxPianoPlayer", "YouTubeWorkspace");
        }

        Directory.CreateDirectory(_workspaceRoot);
    }

    public string GetJobDirectory(string jobId)
    {
        ValidateJobId(jobId);

        string rootFull = Path.GetFullPath(_workspaceRoot);
        string jobDir = Path.GetFullPath(Path.Combine(rootFull, jobId));

        string prefix = rootFull.EndsWith(Path.DirectorySeparatorChar.ToString())
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;

        if (!jobDir.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"잘못된 작업 경로입니다: {jobId}");
        }

        Directory.CreateDirectory(jobDir);
        return jobDir;
    }

    public string GetSourceWavPath(string jobId)
    {
        string dir = GetJobDirectory(jobId);
        return Path.Combine(dir, "source.wav");
    }

    public string GetOutputTemplate(string jobId)
    {
        string dir = GetJobDirectory(jobId);
        return Path.Combine(dir, "source.%(ext)s");
    }

    public void CleanJob(string jobId)
    {
        ValidateJobId(jobId);

        string rootFull = Path.GetFullPath(_workspaceRoot);
        string jobDir = Path.GetFullPath(Path.Combine(rootFull, jobId));

        string prefix = rootFull.EndsWith(Path.DirectorySeparatorChar.ToString())
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;

        if (!jobDir.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"루트 외 경로 삭제 불가: {jobId}");
        }

        if (Directory.Exists(jobDir))
        {
            try
            {
                Directory.Delete(jobDir, true);
            }
            catch
            {
                // Ignore transient cleanup errors
            }
        }
    }

    public void CleanAll()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            foreach (var dir in Directory.GetDirectories(_workspaceRoot))
            {
                try
                {
                    Directory.Delete(dir, true);
                }
                catch
                {
                    // Ignore transient cleanup errors
                }
            }
        }
    }

    public static void ValidateJobId(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId) || !JobIdRegex.IsMatch(jobId))
        {
            throw new ArgumentException($"유효하지 않은 JobId입니다: '{jobId}'", nameof(jobId));
        }
    }
}
