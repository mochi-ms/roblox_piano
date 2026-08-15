using System.Diagnostics;
using RobloxPiano.Playback.Windows.Input;

namespace RobloxPiano.Playback.Windows.Playback;

public class KeyStateManager : IDisposable
{
    private readonly IPlaybackBackend _backend;
    private readonly HashSet<string> _pressedPhysicalKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activeModifiers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    private readonly TimeSpan _idleTimeout;
    private readonly Timer? _watchdogTimer;
    private long _lastActivityTimestamp;
    private bool _disposed;

    public KeyStateManager(IPlaybackBackend backend, double idleTimeoutSeconds = 2.0, bool enableWatchdog = false)
    {
        _backend = backend;
        _idleTimeout = TimeSpan.FromSeconds(idleTimeoutSeconds);
        _lastActivityTimestamp = Stopwatch.GetTimestamp();

        if (enableWatchdog)
        {
            _watchdogTimer = new Timer(WatchdogCheck, null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
        }
    }

    public IReadOnlySet<string> ActiveModifiers
    {
        get
        {
            lock (_lock)
            {
                return new HashSet<string>(_activeModifiers, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public IReadOnlySet<string> ActiveKeys
    {
        get
        {
            lock (_lock)
            {
                return new HashSet<string>(_pressedPhysicalKeys, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public void SetModifier(string modifier, bool active)
    {
        var modUpper = modifier.ToUpperInvariant();
        var modLower = modifier.ToLowerInvariant();

        lock (_lock)
        {
            _lastActivityTimestamp = Stopwatch.GetTimestamp();

            if (active && !_activeModifiers.Contains(modUpper))
            {
                _backend.KeyDown(modLower);
                _activeModifiers.Add(modUpper);
            }
            else if (!active && _activeModifiers.Contains(modUpper))
            {
                _backend.KeyUp(modLower);
                _activeModifiers.Remove(modUpper);
            }
        }
    }

    public void PressPhysicalKey(string physicalKey)
    {
        var keyLower = physicalKey.ToLowerInvariant();
        lock (_lock)
        {
            _lastActivityTimestamp = Stopwatch.GetTimestamp();
            if (_pressedPhysicalKeys.Contains(keyLower))
            {
                _backend.KeyUp(keyLower);
            }
            _backend.KeyDown(keyLower);
            _pressedPhysicalKeys.Add(keyLower);
        }
    }

    public void ReleasePhysicalKey(string physicalKey)
    {
        var keyLower = physicalKey.ToLowerInvariant();
        lock (_lock)
        {
            _lastActivityTimestamp = Stopwatch.GetTimestamp();
            if (_pressedPhysicalKeys.Contains(keyLower))
            {
                _backend.KeyUp(keyLower);
                _pressedPhysicalKeys.Remove(keyLower);
            }
        }
    }

    public void ReleaseAll()
    {
        lock (_lock)
        {
            _lastActivityTimestamp = Stopwatch.GetTimestamp();

            foreach (var k in _pressedPhysicalKeys.ToList())
            {
                try
                {
                    _backend.KeyUp(k);
                }
                catch
                {
                    // Best-effort release
                }
            }
            _pressedPhysicalKeys.Clear();

            foreach (var mod in _activeModifiers.ToList())
            {
                try
                {
                    _backend.KeyUp(mod.ToLowerInvariant());
                }
                catch
                {
                    // Best-effort release
                }
            }
            _activeModifiers.Clear();

            try
            {
                _backend.ReleaseAll();
            }
            catch
            {
                // Best-effort release
            }
        }
    }

    private void WatchdogCheck(object? state)
    {
        lock (_lock)
        {
            if (_pressedPhysicalKeys.Count == 0 && _activeModifiers.Count == 0)
            {
                return;
            }

            var elapsed = Stopwatch.GetElapsedTime(_lastActivityTimestamp);
            if (elapsed > _idleTimeout)
            {
                ReleaseAll();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _watchdogTimer?.Dispose();
        ReleaseAll();
    }
}
