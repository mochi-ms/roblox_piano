namespace RobloxPiano.Playback.Windows.WindowsIntegration;

public class RobloxTargetWindowService : IRobloxTargetWindowService
{
    private static readonly HashSet<string> AllowedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "RobloxPlayerBeta",
        "RobloxPlayerLauncher",
        "RobloxPlayer"
    };

    private readonly IWindowApi _windowApi;
    private readonly object _lock = new();
    private RobloxWindowInfo? _currentTarget;
    private List<RobloxWindowInfo> _availableTargets = new();

    public RobloxWindowInfo? CurrentTarget
    {
        get
        {
            lock (_lock) return _currentTarget;
        }
        private set
        {
            bool changed = false;
            lock (_lock)
            {
                if (_currentTarget != value)
                {
                    _currentTarget = value;
                    changed = true;
                }
            }
            if (changed)
            {
                TargetChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool HasTarget => CurrentTarget != null && ValidateTarget(CurrentTarget);

    public IReadOnlyList<RobloxWindowInfo> AvailableTargets
    {
        get
        {
            lock (_lock) return _availableTargets.ToList();
        }
    }

    public event EventHandler? TargetChanged;
    public event EventHandler? AvailableTargetsChanged;

    public RobloxTargetWindowService(IWindowApi? windowApi = null)
    {
        _windowApi = windowApi ?? new Win32WindowApi();
    }

    public IReadOnlyList<RobloxWindowInfo> FindRobloxWindows()
    {
        var results = new List<RobloxWindowInfo>();
        var hwnds = _windowApi.EnumTopLevelWindows();

        foreach (var hwnd in hwnds)
        {
            if (!_windowApi.IsWindow(hwnd)) continue;
            if (!_windowApi.IsWindowVisible(hwnd)) continue;

            int pid = _windowApi.GetWindowProcessId(hwnd);
            if (pid <= 0) continue;

            string procName = _windowApi.GetProcessName(pid);
            if (!AllowedProcessNames.Contains(procName)) continue;

            string className = _windowApi.GetWindowClassName(hwnd);
            if (string.Equals(className, "Progman", StringComparison.OrdinalIgnoreCase)) continue;

            string title = _windowApi.GetWindowTitle(hwnd);
            bool isMinimized = _windowApi.IsIconic(hwnd);

            results.Add(new RobloxWindowInfo(
                Hwnd: hwnd,
                ProcessId: pid,
                ProcessName: procName,
                Title: title,
                ClassName: className,
                IsVisible: true,
                IsMinimized: isMinimized
            ));
        }

        return results;
    }

    public RobloxWindowInfo? FindBestTarget()
    {
        var windows = FindRobloxWindows();
        if (windows.Count == 1)
        {
            return windows[0];
        }
        return null;
    }

    public void Refresh()
    {
        var windows = FindRobloxWindows();
        bool targetsChanged = false;

        lock (_lock)
        {
            _availableTargets = windows.ToList();
            targetsChanged = true;

            if (_currentTarget != null)
            {
                var existing = windows.FirstOrDefault(w => w.Hwnd == _currentTarget.Hwnd && w.ProcessId == _currentTarget.ProcessId);
                if (existing != null)
                {
                    _currentTarget = existing;
                }
                else
                {
                    _currentTarget = null;
                }
            }
            else
            {
                if (windows.Count == 1)
                {
                    _currentTarget = windows[0];
                }
            }
        }

        if (targetsChanged)
        {
            AvailableTargetsChanged?.Invoke(this, EventArgs.Empty);
            TargetChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool SetTarget(nint hwnd)
    {
        var windows = FindRobloxWindows();
        var target = windows.FirstOrDefault(w => w.Hwnd == hwnd);
        if (target != null && ValidateTarget(target))
        {
            lock (_lock)
            {
                _availableTargets = windows.ToList();
            }
            CurrentTarget = target;
            AvailableTargetsChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        return false;
    }

    public void ClearTarget()
    {
        CurrentTarget = null;
    }

    public bool ValidateTarget(RobloxWindowInfo? target = null)
    {
        target ??= CurrentTarget;
        if (target == null) return false;

        if (target.Hwnd == nint.Zero) return false;
        if (!_windowApi.IsWindow(target.Hwnd)) return false;
        if (!_windowApi.IsProcessRunning(target.ProcessId)) return false;

        int currentPid = _windowApi.GetWindowProcessId(target.Hwnd);
        if (currentPid != target.ProcessId) return false;

        string currentProcName = _windowApi.GetProcessName(currentPid);
        if (!AllowedProcessNames.Contains(currentProcName)) return false;

        return true;
    }
}
