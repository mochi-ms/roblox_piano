using RobloxPiano.Core.YouTube;

namespace RobloxPiano.Infrastructure.YouTube;

public class YtDlpToolLocator : IYtDlpToolLocator
{
    private readonly IYtDlpProcessRunner _processRunner;

    public YtDlpToolLocator(IYtDlpProcessRunner? processRunner = null)
    {
        _processRunner = processRunner ?? new YtDlpProcessRunner();
    }

    public async Task<YouTubeToolStatus> LocateAsync(string? explicitPath = null, CancellationToken ct = default)
    {
        // 1. Explicit path
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            if (!File.Exists(explicitPath))
            {
                return YouTubeToolStatus.Unavailable($"지정된 yt-dlp 경로에 파일이 존재하지 않습니다: {explicitPath}");
            }

            var explicitResult = await ProbeVersionAsync(explicitPath, ct);
            return explicitResult ?? YouTubeToolStatus.Unavailable($"지정된 yt-dlp 실행 실패: {explicitPath}");
        }

        // 2. Application-local tools directories
        var localCandidates = GetLocalCandidatePaths();
        foreach (var candidate in localCandidates)
        {
            if (File.Exists(candidate))
            {
                var probed = await ProbeVersionAsync(candidate, ct);
                if (probed != null)
                {
                    return probed;
                }
            }
        }

        // 3. System PATH
        var pathCandidates = GetPathCandidates();
        foreach (var candidate in pathCandidates)
        {
            if (File.Exists(candidate))
            {
                var probed = await ProbeVersionAsync(candidate, ct);
                if (probed != null)
                {
                    return probed;
                }
            }
        }

        return YouTubeToolStatus.Unavailable(YouTubeError.YtDlpNotFound);
    }

    private async Task<YouTubeToolStatus?> ProbeVersionAsync(string path, CancellationToken ct)
    {
        try
        {
            var res = await _processRunner.RunProcessAsync(
                path,
                new[] { "--version" },
                timeout: TimeSpan.FromSeconds(5),
                ct: ct
            );

            if (res.IsSuccess && !string.IsNullOrWhiteSpace(res.StandardOutput))
            {
                string version = res.StandardOutput.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                return YouTubeToolStatus.Available(path, version);
            }
        }
        catch
        {
            // Ignore probe errors and continue
        }

        return null;
    }

    private static IEnumerable<string> GetLocalCandidatePaths()
    {
        var candidates = new List<string>();

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localAppData))
        {
            candidates.Add(Path.Combine(localAppData, "RobloxPianoPlayer", "tools", "yt-dlp.exe"));
        }

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDir))
        {
            candidates.Add(Path.Combine(baseDir, "tools", "yt-dlp.exe"));
            candidates.Add(Path.Combine(baseDir, "yt-dlp.exe"));
        }

        return candidates;
    }

    private static IEnumerable<string> GetPathCandidates()
    {
        var candidates = new List<string>();
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return candidates;

        var dirs = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in dirs)
        {
            try
            {
                string trimmed = dir.Trim();
                if (!string.IsNullOrEmpty(trimmed) && Directory.Exists(trimmed))
                {
                    candidates.Add(Path.Combine(trimmed, "yt-dlp.exe"));
                }
            }
            catch
            {
                // Ignore invalid PATH entries
            }
        }

        return candidates;
    }
}
