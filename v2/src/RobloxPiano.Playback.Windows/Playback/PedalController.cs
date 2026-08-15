using RobloxPiano.Playback.Windows.Input;

namespace RobloxPiano.Playback.Windows.Playback;

public class PedalController : IDisposable
{
    private readonly IPlaybackBackend _backend;
    private readonly string _pedalKey;
    private readonly object _lock = new();
    private bool _isDown;

    public bool IsDown
    {
        get
        {
            lock (_lock)
            {
                return _isDown;
            }
        }
    }

    public PedalController(IPlaybackBackend backend, string pedalKey = "space")
    {
        _backend = backend;
        _pedalKey = pedalKey;
    }

    public void PedalDown()
    {
        lock (_lock)
        {
            if (!_isDown)
            {
                _backend?.KeyDown(_pedalKey);
                _isDown = true;
            }
        }
    }

    public void PedalUp()
    {
        lock (_lock)
        {
            if (_isDown)
            {
                _backend?.KeyUp(_pedalKey);
                _isDown = false;
            }
        }
    }

    public void Release()
    {
        PedalUp();
    }

    public void Dispose()
    {
        Release();
    }
}
