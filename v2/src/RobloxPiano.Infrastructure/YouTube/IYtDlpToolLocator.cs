using RobloxPiano.Core.YouTube;

namespace RobloxPiano.Infrastructure.YouTube;

public interface IYtDlpToolLocator
{
    Task<YouTubeToolStatus> LocateAsync(string? explicitPath = null, CancellationToken ct = default);
}
