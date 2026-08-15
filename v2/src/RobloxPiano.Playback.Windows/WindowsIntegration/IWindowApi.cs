namespace RobloxPiano.Playback.Windows.WindowsIntegration;

public delegate bool EnumWindowsProc(nint hWnd, nint lParam);

public interface IWindowApi
{
    IEnumerable<nint> EnumTopLevelWindows();
    bool IsWindow(nint hwnd);
    bool IsWindowVisible(nint hwnd);
    string GetWindowTitle(nint hwnd);
    string GetWindowClassName(nint hwnd);
    int GetWindowProcessId(nint hwnd);
    string GetProcessName(int processId);
    bool IsProcessRunning(int processId);
    nint GetForegroundWindow();
    bool SetForegroundWindow(nint hwnd);
    bool ShowWindow(nint hwnd, int nCmdShow);
    bool IsIconic(nint hwnd);
    bool RegisterHotKey(nint hwnd, int id, uint fsModifiers, uint vk);
    bool UnregisterHotKey(nint hwnd, int id);
}
