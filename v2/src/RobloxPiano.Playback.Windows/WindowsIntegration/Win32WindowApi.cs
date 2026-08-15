using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace RobloxPiano.Playback.Windows.WindowsIntegration;

public class Win32WindowApi : IWindowApi
{
    public const int SW_RESTORE = 9;
    public const int SW_SHOW = 5;
    public const int SW_SHOWDEFAULT = 10;

    [DllImport("user32.dll", EntryPoint = "EnumWindows")]
    private static extern bool NativeEnumWindows(NativeEnumWindowsProc lpEnumFunc, nint lParam);
    private delegate bool NativeEnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll", EntryPoint = "IsWindow")]
    private static extern bool NativeIsWindow(nint hWnd);

    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
    private static extern bool NativeIsWindowVisible(nint hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int NativeGetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int NativeGetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId", SetLastError = true)]
    private static extern uint NativeGetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern nint NativeGetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    private static extern bool NativeSetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", EntryPoint = "ShowWindow")]
    private static extern bool NativeShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll", EntryPoint = "IsIconic")]
    private static extern bool NativeIsIconic(nint hWnd);

    [DllImport("user32.dll", EntryPoint = "RegisterHotKey", SetLastError = true)]
    private static extern bool NativeRegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", EntryPoint = "UnregisterHotKey", SetLastError = true)]
    private static extern bool NativeUnregisterHotKey(nint hWnd, int id);

    public IEnumerable<nint> EnumTopLevelWindows()
    {
        var list = new List<nint>();
        try
        {
            NativeEnumWindows((hwnd, _) =>
            {
                list.Add(hwnd);
                return true;
            }, nint.Zero);
        }
        catch
        {
            // Graceful fallback
        }
        return list;
    }

    public bool IsWindow(nint hwnd) => hwnd != nint.Zero && NativeIsWindow(hwnd);

    public bool IsWindowVisible(nint hwnd) => hwnd != nint.Zero && NativeIsWindowVisible(hwnd);

    public string GetWindowTitle(nint hwnd)
    {
        if (hwnd == nint.Zero) return string.Empty;
        var sb = new StringBuilder(512);
        int len = NativeGetWindowText(hwnd, sb, sb.Capacity);
        return len > 0 ? sb.ToString() : string.Empty;
    }

    public string GetWindowClassName(nint hwnd)
    {
        if (hwnd == nint.Zero) return string.Empty;
        var sb = new StringBuilder(256);
        int len = NativeGetClassName(hwnd, sb, sb.Capacity);
        return len > 0 ? sb.ToString() : string.Empty;
    }

    public int GetWindowProcessId(nint hwnd)
    {
        if (hwnd == nint.Zero) return 0;
        NativeGetWindowThreadProcessId(hwnd, out uint pid);
        return (int)pid;
    }

    public string GetProcessName(int processId)
    {
        if (processId <= 0) return string.Empty;
        try
        {
            using var proc = Process.GetProcessById(processId);
            return proc.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    public bool IsProcessRunning(int processId)
    {
        if (processId <= 0) return false;
        try
        {
            using var proc = Process.GetProcessById(processId);
            return !proc.HasExited;
        }
        catch
        {
            return false;
        }
    }

    public nint GetForegroundWindow() => NativeGetForegroundWindow();

    public bool SetForegroundWindow(nint hwnd) => hwnd != nint.Zero && NativeSetForegroundWindow(hwnd);

    public bool ShowWindow(nint hwnd, int nCmdShow) => hwnd != nint.Zero && NativeShowWindow(hwnd, nCmdShow);

    public bool IsIconic(nint hwnd) => hwnd != nint.Zero && NativeIsIconic(hwnd);

    public bool RegisterHotKey(nint hwnd, int id, uint fsModifiers, uint vk) => NativeRegisterHotKey(hwnd, id, fsModifiers, vk);

    public bool UnregisterHotKey(nint hwnd, int id) => NativeUnregisterHotKey(hwnd, id);
}
