namespace RobloxPiano.Playback.Windows.Input;

public enum BackendAction
{
    KeyDown,
    KeyUp
}

public record PlaybackBackendEvent(double Timestamp, BackendAction Action, string Key);

public interface IPlaybackBackend : IDisposable
{
    void KeyDown(string key);
    void KeyUp(string key);
    void ReleaseAll();
}
