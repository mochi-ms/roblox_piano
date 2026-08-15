namespace RobloxPiano.Playback.Windows.WindowsIntegration;

public enum HotkeyAction
{
    Play,
    PauseResume,
    Stop
}

public interface IGlobalHotkeyService : IDisposable
{
    IReadOnlyDictionary<HotkeyAction, bool> RegistrationStatus { get; }

    bool RegisterHotkeys(nint windowHandle);
    void UnregisterHotkeys();
    nint ProcessWindowMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled);

    event EventHandler<HotkeyAction>? HotkeyPressed;
}
