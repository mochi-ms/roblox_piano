using System.IO;

namespace RobloxPiano.Infrastructure.Audio;

public class FfmpegToolLocator : IFfmpegToolLocator
{
    private readonly IFfmpegProcessRunner _runner;

    public FfmpegToolLocator(IFfmpegProcessRunner? runner = null)
    {
        _runner = runner ?? new FfmpegProcessRunner();
    }

    public async Task<FfmpegToolInfo> LocateToolsAsync(
        string? explicitFfmpegPath = null,
        string? explicitFfprobePath = null,
        CancellationToken ct = default)
    {
        // 1. Locate FFmpeg
        string? resolvedFfmpeg = null;
        string? ffmpegVersion = null;

        if (!string.IsNullOrWhiteSpace(explicitFfmpegPath))
        {
            (resolvedFfmpeg, ffmpegVersion) = await TryValidateToolAsync(explicitFfmpegPath, ct);
        }

        if (resolvedFfmpeg == null)
        {
            foreach (var candidate in GetCandidatePaths("ffmpeg.exe"))
            {
                (resolvedFfmpeg, ffmpegVersion) = await TryValidateToolAsync(candidate, ct);
                if (resolvedFfmpeg != null) break;
            }
        }

        // 2. Locate FFprobe
        string? resolvedFfprobe = null;
        string? ffprobeVersion = null;

        if (!string.IsNullOrWhiteSpace(explicitFfprobePath))
        {
            (resolvedFfprobe, ffprobeVersion) = await TryValidateToolAsync(explicitFfprobePath, ct);
        }

        // If FFmpeg was found, check sibling ffprobe.exe
        if (resolvedFfprobe == null && resolvedFfmpeg != null)
        {
            string? dir = Path.GetDirectoryName(resolvedFfmpeg);
            if (!string.IsNullOrEmpty(dir))
            {
                string sibling = Path.Combine(dir, "ffprobe.exe");
                (resolvedFfprobe, ffprobeVersion) = await TryValidateToolAsync(sibling, ct);
            }
        }

        if (resolvedFfprobe == null)
        {
            foreach (var candidate in GetCandidatePaths("ffprobe.exe"))
            {
                (resolvedFfprobe, ffprobeVersion) = await TryValidateToolAsync(candidate, ct);
                if (resolvedFfprobe != null) break;
            }
        }

        return new FfmpegToolInfo(
            FfmpegPath: resolvedFfmpeg,
            FfprobePath: resolvedFfprobe,
            IsFfmpegAvailable: resolvedFfmpeg != null,
            IsFfprobeAvailable: resolvedFfprobe != null,
            FfmpegVersionLine: ffmpegVersion,
            FfprobeVersionLine: ffprobeVersion
        );
    }

    private static IEnumerable<string> GetCandidatePaths(string executableName)
    {
        // App-local tools directory
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        yield return Path.Combine(baseDir, "tools", executableName);
        yield return Path.Combine(baseDir, executableName);

        // LocalAppData tools directory
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(localAppData, "RobloxPianoPlayer", "tools", executableName);

        // PATH environment variable lookup
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            var directories = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var dir in directories)
            {
                string candidate = Path.Combine(dir, executableName);
                yield return candidate;
            }
        }
    }

    private async Task<(string? Path, string? VersionLine)> TryValidateToolAsync(string candidatePath, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(candidatePath) || !File.Exists(candidatePath))
            {
                return (null, null);
            }

            var result = await _runner.RunProcessAsync(
                candidatePath,
                new[] { "-version" },
                timeout: TimeSpan.FromSeconds(3),
                ct: ct);

            if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                using var reader = new StringReader(result.StandardOutput);
                string? firstLine = reader.ReadLine();
                return (Path.GetFullPath(candidatePath), firstLine?.Trim());
            }
        }
        catch
        {
            // Ignore validation errors and try next candidate
        }

        return (null, null);
    }
}
