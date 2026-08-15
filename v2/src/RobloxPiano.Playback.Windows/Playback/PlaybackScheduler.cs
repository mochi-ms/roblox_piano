using System.Diagnostics;
using RobloxPiano.Core.Music;

namespace RobloxPiano.Playback.Windows.Playback;

public class PlaybackScheduler : IDisposable, IAsyncDisposable
{
    private readonly ChordEngine _chordEngine;
    private readonly KeyStateManager _keyState;
    private readonly PedalController _pedal;

    private MusicTimeline? _timeline;
    private PlaybackState _state = PlaybackState.Idle;

    private double _currentTime;
    private double _totalTime;
    private int _playedNotes;
    private int _skippedNotes;

    private int _countdownSeconds = 3;
    private double _speed = 1.0;
    private double _activeSpeed = 1.0;
    private int _transpose;
    private bool _enableRh = true;
    private bool _enableLh = true;
    private Dictionary<int, bool>? _trackFilter;

    // Monotonic anchor state
    private double _songAnchorSeconds;
    private long _perfAnchorTicks;

    private long _currentGeneration;
    private CancellationTokenSource? _cts;
    private Task? _workerTask;

    private readonly object _stateLock = new();
    private readonly ManualResetEventSlim _pauseEvent = new(true);
    private readonly AutoResetEvent _controlWakeEvent = new(false);

    private bool _disposed;

    public event EventHandler<PlaybackState>? StateChanged;
    public event EventHandler<PlaybackProgress>? ProgressChanged;
    public event EventHandler<int>? CountdownTick;
    public event EventHandler<IReadOnlyList<NoteEvent>>? ChordStarted;
    public event EventHandler<IReadOnlyList<NoteEvent>>? ChordPlayed;
    public event EventHandler<ChordPlaybackResult>? ChordEnded;
    public event EventHandler<Exception>? PlaybackError;

    public PlaybackState State
    {
        get { lock (_stateLock) return _state; }
    }

    public double CurrentTime
    {
        get { lock (_stateLock) return _currentTime; }
    }

    public double TotalTime
    {
        get { lock (_stateLock) return _totalTime; }
    }

    public int PlayedNoteCount => _playedNotes;
    public int SkippedNoteCount => _skippedNotes;

    public int CountdownSeconds
    {
        get => _countdownSeconds;
        set => _countdownSeconds = Math.Max(0, value);
    }

    public double Speed
    {
        get { lock (_stateLock) return _speed; }
        set => SetSpeed(value);
    }

    public int Transpose
    {
        get { lock (_stateLock) return _transpose; }
        set => SetTranspose(value);
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

    public PlaybackScheduler(
        ChordEngine chordEngine,
        KeyStateManager keyState,
        PedalController? pedal = null)
    {
        _chordEngine = chordEngine;
        _keyState = keyState;
        _pedal = pedal ?? new PedalController(keyState.ActiveKeys.Count > 0 ? null! : null!);
    }

    public void SetTimeline(MusicTimeline timeline)
    {
        ThrowIfDisposed();
        Stop();

        lock (_stateLock)
        {
            _timeline = timeline;
            _totalTime = timeline.Duration;
            _currentTime = 0.0;
            _playedNotes = 0;
            _skippedNotes = 0;
            SetState(PlaybackState.Idle);
            NotifyProgress();
        }
    }

    public void SetTrackFilter(Dictionary<int, bool>? trackFilter)
    {
        _trackFilter = trackFilter;
    }

    public void Play(double? startOffset = null)
    {
        ThrowIfDisposed();
        lock (_stateLock)
        {
            if (_timeline == null) return;

            if (_state == PlaybackState.Paused && !startOffset.HasValue)
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
            _controlWakeEvent.Reset();

            var token = _cts.Token;
            var initialState = (_countdownSeconds > 0) ? PlaybackState.Countdown : PlaybackState.Playing;
            SetState(initialState);

            _workerTask = Task.Run(() => WorkerLoop(generation, token, withCountdown: _countdownSeconds > 0), token);
        }
    }

    public void Pause()
    {
        ThrowIfDisposed();
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
        ThrowIfDisposed();
        lock (_stateLock)
        {
            if (_state == PlaybackState.Paused)
            {
                // If the previous worker task completed or was canceled (e.g. after a paused seek)
                if (_workerTask == null || _workerTask.IsCompleted)
                {
                    long generation = Interlocked.Increment(ref _currentGeneration);
                    _cts = new CancellationTokenSource();
                    _pauseEvent.Set();
                    _controlWakeEvent.Reset();

                    var token = _cts.Token;
                    SetState(PlaybackState.Playing);
                    _workerTask = Task.Run(() => WorkerLoop(generation, token, withCountdown: false), token);
                }
                else
                {
                    SetState(PlaybackState.Playing);
                    _pauseEvent.Set();
                    _controlWakeEvent.Set();
                }
            }
        }
    }

    public void TogglePlayPause()
    {
        ThrowIfDisposed();
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
        Task? taskToWait;
        lock (_stateLock)
        {
            StopInternal();
            _currentTime = 0.0;
            SetState(PlaybackState.Stopped);
            NotifyProgress();
            taskToWait = _workerTask;
        }

        WaitForWorkerExit(taskToWait);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        Task? taskToWait;
        lock (_stateLock)
        {
            StopInternal();
            _currentTime = 0.0;
            SetState(PlaybackState.Stopped);
            NotifyProgress();
            taskToWait = _workerTask;
        }

        if (taskToWait != null)
        {
            try
            {
                await taskToWait.WaitAsync(ct).ConfigureAwait(false);
            }
            catch { }
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

        _controlWakeEvent.Set();
        _pauseEvent.Set(); // unblock if paused
        _keyState.ReleaseAll();
        _pedal.Release();
    }

    private static void WaitForWorkerExit(Task? task)
    {
        if (task == null || task.IsCompleted) return;

        try
        {
            task.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch { }
    }

    public void Seek(double targetSeconds)
    {
        ThrowIfDisposed();
        Task? taskToWait = null;

        lock (_stateLock)
        {
            double clamped = Math.Clamp(targetSeconds, 0.0, _totalTime);
            bool wasPlaying = (_state == PlaybackState.Playing);
            bool wasPaused = (_state == PlaybackState.Paused);

            if (wasPlaying)
            {
                StopInternal();
                taskToWait = _workerTask;

                _currentTime = clamped;
                long generation = Interlocked.Increment(ref _currentGeneration);
                _cts = new CancellationTokenSource();
                _pauseEvent.Set();
                _controlWakeEvent.Reset();

                var token = _cts.Token;
                SetState(PlaybackState.Playing);
                _workerTask = Task.Run(() => WorkerLoop(generation, token, withCountdown: false), token);
            }
            else if (wasPaused)
            {
                StopInternal();
                taskToWait = _workerTask;

                _currentTime = clamped;
                _state = PlaybackState.Paused;
                StateChanged?.Invoke(this, PlaybackState.Paused);
                NotifyProgress();
            }
            else
            {
                _currentTime = clamped;
                if (_state == PlaybackState.Completed)
                {
                    SetState(PlaybackState.Stopped);
                }
                NotifyProgress();
            }
        }

        WaitForWorkerExit(taskToWait);
    }

    public void SetSpeed(double speed)
    {
        ThrowIfDisposed();
        double clampedSpeed = Math.Clamp(speed, 0.25, 3.0);

        lock (_stateLock)
        {
            if (_state == PlaybackState.Playing)
            {
                long now = Stopwatch.GetTimestamp();
                double elapsedWallSec = (double)(now - _perfAnchorTicks) / Stopwatch.Frequency;
                double currentSongPos = _songAnchorSeconds + (elapsedWallSec * _activeSpeed);

                _songAnchorSeconds = Math.Clamp(currentSongPos, 0.0, _totalTime);
                _perfAnchorTicks = now;
                _activeSpeed = clampedSpeed;
                _speed = clampedSpeed;

                _controlWakeEvent.Set();
            }
            else
            {
                _speed = clampedSpeed;
                _activeSpeed = clampedSpeed;
            }
        }
    }

    public void SetTranspose(int semitones)
    {
        ThrowIfDisposed();
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

    private void WorkerLoop(long generation, CancellationToken ct, bool withCountdown)
    {
        try
        {
            // 1. Countdown Phase
            if (withCountdown && _countdownSeconds > 0)
            {
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

            // 2. Prepare event stream starting from _currentTime
            var filteredNotes = _timeline.GetFilteredNotes(_enableRh, _enableLh, _trackFilter);
            var chordGroups = _timeline.BuildChordGroups(filteredNotes);

            var items = new List<PlaybackItem>();
            foreach (var cg in chordGroups)
            {
                if (cg.StartTime >= _currentTime - 0.001)
                {
                    items.Add(new PlaybackItem(cg.StartTime, EventType.Chord, cg, null));
                }
            }

            foreach (var p in _timeline.Pedals)
            {
                if (p.Time >= _currentTime - 0.001)
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

            lock (_stateLock)
            {
                _songAnchorSeconds = _currentTime;
                _perfAnchorTicks = Stopwatch.GetTimestamp();
                _activeSpeed = _speed;
            }

            long lastProgressReportTicks = 0;

            for (int i = 0; i < items.Count; i++)
            {
                if (generation != _currentGeneration || ct.IsCancellationRequested)
                {
                    break;
                }

                var item = items[i];

                if (!WaitForNextEvent(item.Time, ref lastProgressReportTicks, generation, ct))
                {
                    break;
                }

                // Execute event
                if (item.Type == EventType.Chord && item.Chord != null)
                {
                    ChordStarted?.Invoke(this, item.Chord.Notes);
                    ChordPlayed?.Invoke(this, item.Chord.Notes);

                    var result = _chordEngine.PlayChordNotes(item.Chord.Notes, transpose: _transpose, ct: ct);
                    Interlocked.Add(ref _playedNotes, result.PlayedCount);
                    Interlocked.Add(ref _skippedNotes, result.SkippedUnmappedCount + result.SkippedConflictCount);

                    ChordEnded?.Invoke(this, result);
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

    private bool WaitForNextEvent(
        double itemTargetTime,
        ref long lastProgressReportTicks,
        long generation,
        CancellationToken ct)
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
                lock (_stateLock)
                {
                    _perfAnchorTicks += pauseDuration;
                }
            }

            double songAnchor;
            long perfAnchor;
            double speed;

            lock (_stateLock)
            {
                songAnchor = _songAnchorSeconds;
                perfAnchor = _perfAnchorTicks;
                speed = _activeSpeed;
            }

            long now = Stopwatch.GetTimestamp();
            double elapsedWallSec = (double)(now - perfAnchor) / Stopwatch.Frequency;
            double currentSongTime = songAnchor + (elapsedWallSec * speed);

            // Update smooth continuous progress position
            lock (_stateLock)
            {
                _currentTime = Math.Clamp(currentSongTime, 0.0, Math.Max(itemTargetTime, _totalTime));
            }

            // Report progress at ~40 Hz
            if (now - lastProgressReportTicks > (Stopwatch.Frequency / 40))
            {
                NotifyProgress();
                lastProgressReportTicks = now;
            }

            double remainingSongSec = itemTargetTime - currentSongTime;
            if (remainingSongSec <= 0.0005)
            {
                break;
            }

            double remainingWallMs = remainingSongSec * 1000.0 / speed;
            if (remainingWallMs > 15.0)
            {
                int waitSlice = (int)Math.Min(15.0, remainingWallMs - 3.0);
                _controlWakeEvent.WaitOne(waitSlice);
            }
            else if (remainingWallMs > 2.0)
            {
                Thread.Sleep(1);
            }
            else
            {
                double targetWallTicks = perfAnchor + ((itemTargetTime - songAnchor) / speed * Stopwatch.Frequency);
                while (Stopwatch.GetTimestamp() < targetWallTicks)
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
            if (remainingMs > 15.0)
            {
                _controlWakeEvent.WaitOne(15);
            }
            else if (remainingMs > 3.0)
            {
                Thread.Sleep(1);
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

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PlaybackScheduler));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        _controlWakeEvent.Dispose();
        _pauseEvent.Dispose();
        _cts?.Dispose();
        _keyState.Dispose();
        _pedal.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await StopAsync().ConfigureAwait(false);
        _controlWakeEvent.Dispose();
        _pauseEvent.Dispose();
        _cts?.Dispose();
        _keyState.Dispose();
        _pedal.Dispose();
    }
}
