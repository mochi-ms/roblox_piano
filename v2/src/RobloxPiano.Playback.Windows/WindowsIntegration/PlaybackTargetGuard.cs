namespace RobloxPiano.Playback.Windows.WindowsIntegration;

public class PlaybackTargetGuard : IPlaybackTargetGuard
{
    private readonly IRobloxTargetWindowService _targetService;
    private readonly IWindowApi _windowApi;
    private readonly object _lock = new();
    private Timer? _monitoringTimer;
    private Action? _onFocusLostCallback;
    private bool _isMonitoring;
    private bool _disposed;

    public IRobloxTargetWindowService TargetService => _targetService;
    public bool IsMonitoring
    {
        get
        {
            lock (_lock) return _isMonitoring;
        }
    }

    public event EventHandler? TargetLost;
    public event EventHandler? TargetInvalidated;

    public PlaybackTargetGuard(IRobloxTargetWindowService targetService, IWindowApi? windowApi = null)
    {
        _targetService = targetService;
        _windowApi = windowApi ?? new Win32WindowApi();
    }

    public bool ValidateTarget()
    {
        return _targetService.ValidateTarget();
    }

    public async Task<bool> ActivateAndVerifyTargetAsync(CancellationToken ct = default)
    {
        var target = _targetService.CurrentTarget;
        if (target == null || !_targetService.ValidateTarget(target))
        {
            return false;
        }

        // Restore if minimized
        if (_windowApi.IsIconic(target.Hwnd))
        {
            _windowApi.ShowWindow(target.Hwnd, Win32WindowApi.SW_RESTORE);
            await Task.Delay(50, ct).ConfigureAwait(false);
        }

        // Set foreground
        _windowApi.SetForegroundWindow(target.Hwnd);

        // Bounded verification loop (max ~300ms)
        for (int i = 0; i < 15; i++)
        {
            ct.ThrowIfCancellationRequested();
            nint fg = _windowApi.GetForegroundWindow();
            if (fg == target.Hwnd)
            {
                return true;
            }
            await Task.Delay(20, ct).ConfigureAwait(false);
        }

        return _windowApi.GetForegroundWindow() == target.Hwnd;
    }

    public void StartMonitoring(Action onFocusLost)
    {
        lock (_lock)
        {
            StopMonitoringInternal();
            _onFocusLostCallback = onFocusLost;
            _isMonitoring = true;
            _monitoringTimer = new Timer(CheckForeground, null, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
        }
    }

    public void StopMonitoring()
    {
        lock (_lock)
        {
            StopMonitoringInternal();
        }
    }

    private void StopMonitoringInternal()
    {
        _isMonitoring = false;
        _monitoringTimer?.Dispose();
        _monitoringTimer = null;
        _onFocusLostCallback = null;
    }

    private void CheckForeground(object? state)
    {
        Action? callback = null;
        bool focusLost = false;
        bool targetInvalid = false;

        lock (_lock)
        {
            if (!_isMonitoring) return;

            var target = _targetService.CurrentTarget;
            if (target == null || !_targetService.ValidateTarget(target))
            {
                targetInvalid = true;
                callback = _onFocusLostCallback;
                StopMonitoringInternal();
            }
            else
            {
                nint fg = _windowApi.GetForegroundWindow();
                if (fg != target.Hwnd)
                {
                    focusLost = true;
                    callback = _onFocusLostCallback;
                    StopMonitoringInternal();
                }
            }
        }

        if (targetInvalid)
        {
            TargetInvalidated?.Invoke(this, EventArgs.Empty);
            callback?.Invoke();
        }
        else if (focusLost)
        {
            TargetLost?.Invoke(this, EventArgs.Empty);
            callback?.Invoke();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopMonitoring();
    }
}
