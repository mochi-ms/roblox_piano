namespace RobloxPiano.Playback.Windows.Input;

/// <summary>
/// Marker interface indicating that a playback backend targets an external process (e.g. Roblox)
/// and strictly requires a valid, verified Roblox foreground target window before playback begins.
/// </summary>
public interface ITargetedPlaybackBackend
{
}
