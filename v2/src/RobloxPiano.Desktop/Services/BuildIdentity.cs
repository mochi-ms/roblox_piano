using System.IO;
using System.Reflection;

namespace RobloxPiano.Desktop.Services;

public static class BuildIdentity
{
    public static string Version { get; }
    public static string GitSha { get; }
    public static string Configuration { get; }
    public static string BuildTime { get; }
    public static string FullIdentity { get; }

    static BuildIdentity()
    {
        var asm = typeof(BuildIdentity).Assembly;
        var infoVerAttr = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        string rawInfoVer = infoVerAttr?.InformationalVersion ?? "2.0.0";

        // Extract git sha if present after '+'
        string sha = "HEAD";
        if (rawInfoVer.Contains('+'))
        {
            var parts = rawInfoVer.Split('+');
            rawInfoVer = parts[0];
            sha = parts[1].Length >= 7 ? parts[1][..7] : parts[1];
        }

        Version = string.IsNullOrEmpty(rawInfoVer) ? "2.0.0" : rawInfoVer;
        GitSha = sha;

#if DEBUG
        Configuration = "Debug";
#else
        Configuration = "Release";
#endif

        string buildDateStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        try
        {
            var loc = asm.Location;
            if (!string.IsNullOrEmpty(loc) && File.Exists(loc))
            {
                buildDateStr = File.GetLastWriteTime(loc).ToString("yyyy-MM-dd HH:mm");
            }
        }
        catch
        {
            // Fallback to runtime date
        }

        BuildTime = buildDateStr;
        FullIdentity = $"Build {GitSha} · {Configuration} · {BuildTime}";
    }
}
