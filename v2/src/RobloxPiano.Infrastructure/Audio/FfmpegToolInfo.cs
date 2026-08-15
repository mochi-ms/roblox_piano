namespace RobloxPiano.Infrastructure.Audio;

public record FfmpegToolInfo(
    string? FfmpegPath,
    string? FfprobePath,
    bool IsFfmpegAvailable,
    bool IsFfprobeAvailable,
    string? FfmpegVersionLine = null,
    string? FfprobeVersionLine = null
)
{
    public bool IsFullyAvailable => IsFfmpegAvailable && IsFfprobeAvailable;

    public static FfmpegToolInfo Unavailable => new(null, null, false, false);
}
