namespace RobloxPiano.Core.YouTube;

public record YouTubeToolStatus(
    bool IsAvailable,
    string? ExecutablePath,
    string? Version,
    string StatusMessage
)
{
    public static YouTubeToolStatus Available(string executablePath, string version) =>
        new(true, executablePath, version, $"● yt-dlp 준비됨 (v{version})");

    public static YouTubeToolStatus Unavailable(string message) =>
        new(false, null, null, $"▲ yt-dlp 미설치: {message}");
}
