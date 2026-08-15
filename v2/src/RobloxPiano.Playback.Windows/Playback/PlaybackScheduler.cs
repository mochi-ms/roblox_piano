using System.Diagnostics;
using RobloxPiano.Core.Music;

namespace RobloxPiano.Playback.Windows.Playback;

public class PlaybackScheduler : IDisposable
{
    private readonly ChordEngine _chordEngine;
    private readonly KeyStateManager _keyState;
    private readonly PedalController _pedal;

    private MusicTimeline? _timeline;
    private PlaybackState _state = PlaybackState.Idle;

    private double _speed = 1.0;
    private int _transpose = 0;
    private int _countdownSeconds = 3;
    private bool _enableRh = true;
    private bool _enableLh = true;
    private Dictionary<int, bool>? _trackFilter;

    private double _currentTime;
    private double _totalTime;
    private int _playedNotes;
    private int _skippedNotes;

    private long _currentGeneration;
    private CancellationTokenSource? _cts;
    private readonly ManualResetEventSlim _pauseEvent = new(true);
    private readonly object _stateLock = new();
    private bool _disposed;

    // Events
    public event EventHandler<PlaybackState>? StateChanged;
    public event EventHandler<PlaybackProgress>? ProgressChanged;
    public event EventHandler<int>? CountdownTick;
    public event EventHandler<IReadOnlyList<NoteEvent>>? ChordPlayed;
    public event EventHandler<Exception>? PlaybackError;

    public PlaybackState State
    {
        get
        {
            lock (_stateLock) return _state;
        }
    }

    public double CurrentTime
    {
        get
        {
            lock (_stateLock) return _currentTime;
        }
    }

    public double TotalTime => _totalTime;

    public double Speed
    {
        get => _speed;
        set => SetSpeed(value);
    }

    public int Transpose
    {
        get => _transpose;
        set => SetTranspose(value);
    }

    public int CountdownSeconds
    {
        get => _countdownSeconds;
        set => _countdownSeconds = Math.Clamp(value, 0, 10);
    }

    public bool EnableRH
    {
        get => _enableRh;
        set => _enableRh = value;
    }

    public bool EnableLH
    {
        get => _enableLh;
        set => _enableLh = value;
    }

    public Dictionary<int, bool>? TrackFilter
    {
        get => _trackFilter;
        set => _trackFilter = value;
    }

    public int PlayedNoteCount => _playedNotes;
    public int SkippedNoteCount => _skippedNotes;

    public PlaybackScheduler(ChordEngine chordEngine, KeyStateManager keyState, PedalController? pedal = null)
    {
        _chordEngine = chordEngine;
        _keyState = keyState;
        _pedal = pedal ?? new PedalController(keyState.ActiveKeys is not null ? new Input.DryRunPlaybackBackend() : null!);
    }

    public void SetTimeline(MusicTimeline? timeline)
    {
        Stop();
        lock (_stateLock)
        {
            _timeline = timeline;
            _currentTime = 0.0;
            _totalTime = timeline?.Duration ?? 0.0;
            _playedNotes = 0;
            _skippedNotes = 0;
            SetState(timeline == null ? PlaybackState.Idle : PlaybackState.Stopped);
            NotifyProgress();
        }
    }

    public void Play(double? startOffset = null)
    {
        lock (_stateLock)
        {
            if (_timeline == null || _timeline.Notes.Count == 0)
            {
                return;
            }

            if (_state == PlaybackState.Paused)
            {
                Resume();
                return;
            }

            StopInternal();

            if (startOffset.HasValue)
            {
                _currentTime = Math.Clamp(startOffset.Value, 0.0, _totalTime);
            }
            else if (_currentTime >= _totalTime && _totalTime > 0)
            {
                _currentTime = 0.0;
            }

            long generation = Interlocked.Increment(ref _currentGeneration);
            _cts = new CancellationTokenSource();
            _pauseEvent.Set();

            var token = _cts.Token;
            var initialState = (_countdownSeconds > 0) ? PlaybackState.Countdown : PlaybackState.Playing;
            SetState(initialState);

            Task.Run(() => WorkerLoop(generation, token), token);
        }
    }

    public void Pause()
    {
        lock (_stateLock)
        {
            if (_state == PlaybackState.Playing)
            {
                _pauseEvent.Reset();
                _keyState.ReleaseAll();
                _pedal.Release();
                SetState(PlaybackState.Paused);
            }
        }
    }

    public void Resume()
    {
        lock (_stateLock)
        {
            if (_state == PlaybackState.Paused)
            {
                SetState(PlaybackState.Playing);
                _pauseEvent.Set();
            }
        }
    }

    public void TogglePlayPause()
    {
        lock (_stateLock)
        {
            if (_state == PlaybackState.Playing)
            {
                Pause();
            }
            else if (_state == PlaybackState.Paused)
            {
                Resume();
            }
            else
            {
                Play();
            }
        }
    }

    public void Stop()
    {
        lock (_stateLock)
        {
            StopInternal();
            _currentTime = 0.0;
            SetState(PlaybackState.Stopped);
            NotifyProgress();
        }
    }

    private void StopInternal()
    {
        Interlocked.Increment(ref _currentGeneration);
        try
        {
            _cts?.Cancel();
        }
        catch { }

        _pauseEvent.Set(); // unblock if paused
        _keyState.ReleaseAll();
        _pedal.Release();
    }

    public void Seek(double targetSeconds)
    {
        lock (_stateLock)
        {
            double clamped = Math.Clamp(targetSeconds, 0.0, _totalTime);
            bool wasPlaying = (_state == PlaybackState.Playing);

            if (wasPlaying)
            {
                Play(clamped);
            }
            else
            {
                _currentTime = clamped;
                NotifyProgress();
            }
        }
    }

    public void SetSpeed(double speed)
    {
        _speed = Math.Clamp(speed, 0.25, 3.0);
    }

    public void SetTranspose(int semitones)
    {
        _transpose = Math.Clamp(semitones, -24, 24);
    }

    private void SetState(PlaybackState newState)
    {
        _state = newState;
        StateChanged?.Invoke(this, newState);
    }

    private void NotifyProgress()
    {
        double ratio = _totalTime > 0 ? Math.Clamp(_currentTime / _totalTime, 0.0, 1.0) : 0.0;
        var prog = new PlaybackProgress(_currentTime, _totalTime, ratio, _playedNotes, _skippedNotes);
        ProgressChanged?.Invoke(this, prog);
    }

    private enum EventType { Chord, Pedal }
    private record PlaybackItem(double Time, EventType Type, ChordGroup? Chord, PedalEvent? Pedal);

    private void WorkerLoop(long generation, CancellationToken ct)
    {
        try
        {
            // 1. Countdown Phase
            if (_countdownSeconds > 0)
            {
                lock (_stateLock)
                {
                    if (generation != _currentGeneration || ct.IsCancellationRequested) return;
                    SetState(PlaybackState.Countdown);
                }

                for (int sec = _countdownSeconds; sec > 0; sec--)
                {
                    if (generation != _currentGeneration || ct.IsCancellationRequested) return;
                    CountdownTick?.Invoke(this, sec);

                    long targetCountdownTicks = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
                    if (!PreciseWait(targetCountdownTicks, generation, ct))
                    {
                        return;
                    }
                }
            }

            lock (_stateLock)
            {
                if (generation != _currentGeneration || ct.IsCancellationRequested) return;
                SetState(PlaybackState.Playing);
            }

            if (_timeline == null)
            {
                return;
            }

            // 2. Prepare event stream
            var filteredNotes = _timeline.GetFilteredNotes(_enableRh, _enableLh, _trackFilter);
            var chordGroups = _timeline.BuildChordGroups(filteredNotes);

            var items = new List<PlaybackItem>();
            foreach (var cg in chordGroups)
            {
                if (cg.StartTime >= _currentTime)
                {
                    items.Add(new PlaybackItem(cg.StartTime, EventType.Chord, cg, null));
                }
            }

            foreach (var p in _timeline.Pedals)
            {
                if (p.Time >= _currentTime)
                {
                    items.Add(new PlaybackItem(p.Time, EventType.Pedal, null, p));
                }
            }

            // Order by timestamp, pedal events before chords on ties for proper sustain
            items.Sort((a, b) =>
            {
                int cmp = a.Time.CompareTo(b.Time);
                if (cmp != 0) return cmp;
                return a.Type == EventType.Pedal ? -1 : 1;
            });

            if (items.Count == 0)
            {
                lock (_stateLock)
                {
                    if (generation == _currentGeneration)
                    {
                        _currentTime = _totalTime;
                        NotifyProgress();
                        SetState(PlaybackState.Completed);
                    }
                }
                return;
            }

            double songAnchor = _currentTime;
            long perfAnchor = Stopwatch.GetTimestamp();
            double activeSpeed = _speed;
            long lastProgressReportTicks = 0;

            for (int i = 0; i < items.Count; i++)
            {
                if (generation != _currentGeneration || ct.IsCancellationRequested)
                {
                    break;
                }

                var item = items[i];

                // If speed changed dynamically, re-anchor without position jump
                if (Math.Abs(activeSpeed - _speed) > 0.001)
                {
                    songAnchor = _currentTime;
                    perfAnchor = Stopwatch.GetTimestamp();
                    activeSpeed = _speed;
                }

                double deltaSong = (item.Time - songAnchor) / activeSpeed;
                long targetPerfTicks = perfAnchor + (long)(deltaSong * Stopwatch.Frequency);

                if (!PreciseWaitWithPauseAdjustment(targetPerfTicks, ref perfAnchor, generation, ct))
                {
                    break;
                }

                lock (_stateLock)
                {
                    if (generation != _currentGeneration || ct.IsCancellationRequested) break;
                    _currentTime = item.Time;
                }

                // Throttle progress updates to ~30-60 Hz max
                long now = Stopwatch.GetTimestamp();
                if (now - lastProgressReportTicks > (Stopwatch.Frequency / 40))
                {
                    NotifyProgress();
                    lastProgressReportTicks = now;
                }

                // Execute event
                if (item.Type == EventType.Chord && item.Chord != null)
                {
                    ChordPlayed?.Invoke(this, item.Chord.Notes);
                    _chordEngine.PlayChordNotes(item.Chord.Notes, transpose: _transpose, ct: ct);
                    Interlocked.Add(ref _playedNotes, item.Chord.Notes.Count);
                }
                else if (item.Type == EventType.Pedal && item.Pedal != null)
                {
                    if (item.Pedal.Down)
                    {
                        _pedal.PedalDown();
                    }
                    else
                    {
                        _pedal.PedalUp();
                    }
                }
            }

            lock (_stateLock)
            {
                if (generation == _currentGeneration && !ct.IsCancellationRequested)
                {
                    _currentTime = _totalTime;
                    NotifyProgress();
                    SetState(PlaybackState.Completed);
                }
            }
        }
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                if (generation == _currentGeneration)
                {
                    SetState(PlaybackState.Stopped);
                    PlaybackError?.Invoke(this, ex);
                }
            }
        }
        finally
        {
            _keyState.ReleaseAll();
            _pedal.Release();
        }
    }

    private bool PreciseWait(long targetTicks, long generation, CancellationToken ct)
    {
        while (true)
        {
            if (generation != _currentGeneration || ct.IsCancellationRequested)
            {
                return false;
            }

            long now = Stopwatch.GetTimestamp();
            long remainingTicks = targetTicks - now;

            if (remainingTicks <= 0)
            {
                break;
            }

            double remainingMs = (double)remainingTicks * 1000.0 / Stopwatch.Frequency;
            if (remainingMs > 5.0)
            {
                Thread.Sleep((int)(remainingMs - 3.0));
            }
            else
            {
                while (Stopwatch.GetTimestamp() < targetTicks)
                {
                    if (generation != _currentGeneration || ct.IsCancellationRequested)
                    {
                        return false;
                    }
                    Thread.Yield();
                }
                break;
            }
        }

        return true;
    }

    private bool PreciseWaitWithPauseAdjustment(long targetTicks, ref long perfAnchor, long generation, CancellationToken ct)
    {
        while (true)
        {
            if (generation != _currentGeneration || ct.IsCancellationRequested)
            {
                return false;
            }

            // Handle paused state
            if (!_pauseEvent.IsSet)
            {
                _keyState.ReleaseAll();
                _pedal.Release();
                long pauseStart = Stopwatch.GetTimestamp();

                try
                {
                    _pauseEvent.Wait(ct);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }

                if (generation != _currentGeneration || ct.IsCancellationRequested)
                {
                    return false;
                }

                long pauseDuration = Stopwatch.GetTimestamp() - pauseStart;
                perfAnchor += pauseDuration;
                targetTicks += pauseDuration;
            }

            long now = Stopwatch.GetTimestamp();
            long remainingTicks = targetTicks - now;

            if (remainingTicks <= 0)
            {
                break;
            }

            double remainingMs = (double)remainingTicks * 1000.0 / Stopwatch.Frequency;
            if (remainingMs > 5.0)
            {
                Thread.Sleep((int)(remainingMs - 3.0));
            }
            else
            {
                while (Stopwatch.GetTimestamp() < targetTicks)
                {
                    if (generation != _currentGeneration || ct.IsCancellationRequested)
                    {
                        return false;
                    }
                    Thread.Yield();
                }
                break;
            }
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        _pauseEvent.Dispose();
        _cts?.Dispose();
        _keyState.Dispose();
        _pedal.Dispose();
    }
}
