namespace RobloxPiano.Playback.Windows.WindowsIntegration;

public class GlobalHotkeyService : IGlobalHotkeyService
{
    public const int WM_HOTKEY = 0x0312;

    // Hotkey IDs
    public const int HOTKEY_ID_F6 = 9001;
    public const int HOTKEY_ID_F7 = 9002;
    public const int HOTKEY_ID_ESC = 9003;

    // Virtual key codes
    public const uint VK_F6 = 0x75;
    public const uint VK_F7 = 0x76;
    public const uint VK_ESCAPE = 0x1B;

    private readonly IWindowApi _windowApi;
    private readonly Dictionary<HotkeyAction, bool> _registrationStatus = new();
    private nint _registeredHwnd;
    private bool _disposed;

    public IReadOnlyDictionary<HotkeyAction, bool> RegistrationStatus =>
        new Dictionary<HotkeyAction, bool>(_registrationStatus);

    public event EventHandler<HotkeyAction>? HotkeyPressed;

    public GlobalHotkeyService(IWindowApi? windowApi = null)
    {
        _windowApi = windowApi ?? new Win32WindowApi();
        _registrationStatus[HotkeyAction.Play] = false;
        _registrationStatus[HotkeyAction.PauseResume] = false;
        _registrationStatus[HotkeyAction.Stop] = false;
    }

    public bool RegisterHotkeys(nint windowHandle)
    {
        if (windowHandle == nint.Zero) return false;
        UnregisterHotkeys();

        _registeredHwnd = windowHandle;

        // F6: Play
        bool f6 = _windowApi.RegisterHotKey(windowHandle, HOTKEY_ID_F6, 0, VK_F6);
        _registrationStatus[HotkeyAction.Play] = f6;

        // F7: Pause/Resume
        bool f7 = _windowApi.RegisterHotKey(windowHandle, HOTKEY_ID_F7, 0, VK_F7);
        _registrationStatus[HotkeyAction.PauseResume] = f7;

        // ESC: Stop
        bool esc = _windowApi.RegisterHotKey(windowHandle, HOTKEY_ID_ESC, 0, VK_ESCAPE);
        _registrationStatus[HotkeyAction.Stop] = esc;

        return f6 || f7 || esc;
    }

    public void UnregisterHotkeys()
    {
        if (_registeredHwnd != nint.Zero)
        {
            try { _windowApi.UnregisterHotKey(_registeredHwnd, HOTKEY_ID_F6); } catch { }
            try { _windowApi.UnregisterHotKey(_registeredHwnd, HOTKEY_ID_F7); } catch { }
            try { _windowApi.UnregisterHotKey(_registeredHwnd, HOTKEY_ID_ESC); } catch { }
            _registeredHwnd = nint.Zero;
        }

        _registrationStatus[HotkeyAction.Play] = false;
        _registrationStatus[HotkeyAction.PauseResume] = false;
        _registrationStatus[HotkeyAction.Stop] = false;
    }

    public nint ProcessWindowMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = (int)wParam;
            HotkeyAction? action = id switch
            {
                HOTKEY_ID_F6 => HotkeyAction.Play,
                HOTKEY_ID_F7 => HotkeyAction.PauseResume,
                HOTKEY_ID_ESC => HotkeyAction.Stop,
                _ => null
            };

            if (action.HasValue)
            {
                handled = true;
                HotkeyPressed?.Invoke(this, action.Value);
            }
        }
        return nint.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterHotkeys();
    }
}
