namespace RobloxPiano.Infrastructure.Audio;

public interface IFfmpegToolLocator
{
    Task<FfmpegToolInfo> LocateToolsAsync(
        string? explicitFfmpegPath = null,
        string? explicitFfprobePath = null,
        CancellationToken ct = default);
}
