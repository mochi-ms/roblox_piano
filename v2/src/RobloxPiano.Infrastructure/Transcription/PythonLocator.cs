using System.IO;

namespace RobloxPiano.Infrastructure.Transcription;

public class PythonLocator : IPythonLocator
{
    private readonly IPythonProcessRunner _runner;

    public PythonLocator(IPythonProcessRunner? runner = null)
    {
        _runner = runner ?? new PythonProcessRunner();
    }

    public async Task<(string? PythonPath, string? VersionLine, bool IsValidPython311)> LocatePythonAsync(
        string? explicitPath = null,
        CancellationToken ct = default)
    {
        // 1. Explicit path
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var res = await ValidatePythonCandidateAsync(explicitPath, ct);
            if (res.PythonPath != null) return res;
        }

        // 2. Candidate paths
        foreach (var candidate in GetCandidatePaths())
        {
            var res = await ValidatePythonCandidateAsync(candidate, ct);
            if (res.IsValidPython311)
            {
                return res;
            }
        }

        return (null, null, false);
    }

    private static IEnumerable<string> GetCandidatePaths()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;

        // App-local / dev worker venv
        yield return Path.Combine(baseDir, "python_worker", ".venv", "Scripts", "python.exe");
        yield return Path.Combine(baseDir, "..", "..", "..", "..", "python_worker", ".venv", "Scripts", "python.exe");
        yield return Path.Combine(baseDir, "..", "..", "..", "python_worker", ".venv", "Scripts", "python.exe");

        // LocalAppData python
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(localAppData, "RobloxPianoPlayer", "python", "python.exe");
        yield return Path.Combine(localAppData, "RobloxPianoPlayer", "python_worker", ".venv", "Scripts", "python.exe");

        // System PATH
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            var dirs = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var dir in dirs)
            {
                yield return Path.Combine(dir, "python.exe");
                yield return Path.Combine(dir, "python3.exe");
                yield return Path.Combine(dir, "python3.11.exe");
            }
        }
    }

    private async Task<(string? PythonPath, string? VersionLine, bool IsValidPython311)> ValidatePythonCandidateAsync(
        string candidatePath,
        CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(candidatePath) || !File.Exists(candidatePath))
            {
                return (null, null, false);
            }

            var result = await _runner.RunProcessAsync(
                candidatePath,
                new[] { "--version" },
                timeout: TimeSpan.FromSeconds(3),
                ct: ct);

            string output = (result.StandardOutput + " " + result.StandardError).Trim();
            if (!string.IsNullOrWhiteSpace(output) && output.StartsWith("Python ", StringComparison.OrdinalIgnoreCase))
            {
                bool is311 = output.StartsWith("Python 3.11.", StringComparison.OrdinalIgnoreCase);
                return (Path.GetFullPath(candidatePath), output, is311);
            }
        }
        catch
        {
            // Ignore validation errors and try next candidate
        }

        return (null, null, false);
    }
}
