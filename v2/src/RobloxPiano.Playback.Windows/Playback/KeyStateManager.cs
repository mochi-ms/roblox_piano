using System.Diagnostics;
using RobloxPiano.Playback.Windows.Input;

namespace RobloxPiano.Playback.Windows.Playback;

public class KeyStateManager : IDisposable
{
    private readonly IPlaybackBackend _backend;
    private readonly HashSet<string> _pressedPhysicalKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activeModifiers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private long _releaseAllEpoch;

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

    internal IPlaybackBackend Backend => _backend;

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
        bool changeToActive = false;
        bool changeToInactive = false;
        long epoch;

        lock (_lock)
        {
            epoch = Volatile.Read(ref _releaseAllEpoch);
            _lastActivityTimestamp = Stopwatch.GetTimestamp();

            if (active && !_activeModifiers.Contains(modUpper))
            {
                _activeModifiers.Add(modUpper);
                changeToActive = true;
            }
            else if (!active && _activeModifiers.Contains(modUpper))
            {
                _activeModifiers.Remove(modUpper);
                changeToInactive = true;
            }
        }

        if (changeToActive)
        {
            try
            {
                _backend.KeyDown(modLower);
            }
            catch
            {
                lock (_lock)
                {
                    _activeModifiers.Remove(modUpper);
                }
                throw;
            }

            if (Volatile.Read(ref _releaseAllEpoch) != epoch)
            {
                try { _backend.KeyUp(modLower); } catch { }
                lock (_lock)
                {
                    _activeModifiers.Remove(modUpper);
                }
            }
        }
        else if (changeToInactive)
        {
            try
            {
                _backend.KeyUp(modLower);
            }
            catch
            {
                lock (_lock)
                {
                    _activeModifiers.Add(modUpper);
                }
                throw;
            }
        }
    }

    public void PressPhysicalKey(string physicalKey)
    {
        var keyLower = physicalKey.ToLowerInvariant();
        bool alreadyPressed;
        long epoch;

        lock (_lock)
        {
            epoch = Volatile.Read(ref _releaseAllEpoch);
            _lastActivityTimestamp = Stopwatch.GetTimestamp();
            alreadyPressed = _pressedPhysicalKeys.Contains(keyLower);
            _pressedPhysicalKeys.Add(keyLower);
        }

        if (alreadyPressed)
        {
            try { _backend.KeyUp(keyLower); } catch { }
        }

        try
        {
            _backend.KeyDown(keyLower);
        }
        catch
        {
            lock (_lock)
            {
                _pressedPhysicalKeys.Remove(keyLower);
            }
            throw;
        }

        if (Volatile.Read(ref _releaseAllEpoch) != epoch)
        {
            try { _backend.KeyUp(keyLower); } catch { }
            lock (_lock)
            {
                _pressedPhysicalKeys.Remove(keyLower);
            }
        }
    }

    public void ReleasePhysicalKey(string physicalKey)
    {
        var keyLower = physicalKey.ToLowerInvariant();
        bool wasPressed;
        lock (_lock)
        {
            _lastActivityTimestamp = Stopwatch.GetTimestamp();
            wasPressed = _pressedPhysicalKeys.Remove(keyLower);
        }

        if (wasPressed)
        {
            try
            {
                _backend.KeyUp(keyLower);
            }
            catch
            {
                lock (_lock)
                {
                    _pressedPhysicalKeys.Add(keyLower);
                }
                throw;
            }
        }
    }

    public void ReleaseAll()
    {
        List<string> keysToRelease;
        List<string> modsToRelease;

        lock (_lock)
        {
            Interlocked.Increment(ref _releaseAllEpoch);
            _lastActivityTimestamp = Stopwatch.GetTimestamp();
            keysToRelease = _pressedPhysicalKeys.ToList();
            _pressedPhysicalKeys.Clear();
            modsToRelease = _activeModifiers.ToList();
            _activeModifiers.Clear();
        }

        foreach (var k in keysToRelease)
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

        foreach (var mod in modsToRelease)
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

        try
        {
            _backend.ReleaseAll();
        }
        catch
        {
            // Best-effort release
        }
    }

    private void WatchdogCheck(object? state)
    {
        bool shouldRelease = false;
        lock (_lock)
        {
            if (_pressedPhysicalKeys.Count == 0 && _activeModifiers.Count == 0)
            {
                return;
            }

            var elapsed = Stopwatch.GetElapsedTime(_lastActivityTimestamp);
            if (elapsed > _idleTimeout)
            {
                shouldRelease = true;
            }
        }

        if (shouldRelease)
        {
            ReleaseAll();
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
