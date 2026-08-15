using System.Diagnostics;

namespace RobloxPiano.Playback.Windows.Input;

public class DryRunPlaybackBackend : IPlaybackBackend
{
    private readonly List<PlaybackBackendEvent> _events = new();
    private readonly HashSet<string> _pressedKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public IReadOnlyList<PlaybackBackendEvent> Events
    {
        get
        {
            lock (_lock)
            {
                return _events.ToList();
            }
        }
    }

    public IReadOnlySet<string> PressedKeys
    {
        get
        {
            lock (_lock)
            {
                return new HashSet<string>(_pressedKeys, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public void KeyDown(string key)
    {
        var keyLower = key.ToLowerInvariant();
        var timestamp = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        lock (_lock)
        {
            _events.Add(new PlaybackBackendEvent(timestamp, BackendAction.KeyDown, keyLower));
            _pressedKeys.Add(keyLower);
        }
    }

    public void KeyUp(string key)
    {
        var keyLower = key.ToLowerInvariant();
        var timestamp = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        lock (_lock)
        {
            _events.Add(new PlaybackBackendEvent(timestamp, BackendAction.KeyUp, keyLower));
            _pressedKeys.Remove(keyLower);
        }
    }

    public void ReleaseAll()
    {
        var timestamp = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        lock (_lock)
        {
            foreach (var k in _pressedKeys.ToList())
            {
                _events.Add(new PlaybackBackendEvent(timestamp, BackendAction.KeyUp, k));
            }
            _pressedKeys.Clear();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _events.Clear();
            _pressedKeys.Clear();
        }
    }

    public void Dispose()
    {
        ReleaseAll();
    }
}
