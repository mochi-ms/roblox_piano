using System.Diagnostics;
using RobloxPiano.Core.Music;

namespace RobloxPiano.Playback.Windows.Playback;

public class PlaybackScheduler : IDisposable, IAsyncDisposable
{
    private sealed class PlaybackRunContext
    {
        public long Generation { get; }
        public CancellationTokenSource Cts { get; }
        public Task? WorkerTask { get; set; }

        public PlaybackRunContext(long generation)
        {
            Generation = generation;
            Cts = new CancellationTokenSource();
        }
    }

    private readonly ChordEngine _chordEngine;
    private readonly KeyStateManager _keyState;
    private readonly PedalController _pedal;
    private readonly TimeSpan _workerTerminationTimeout;

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

    private long _generationCounter;
    private PlaybackRunContext? _activeRun;
    private PlaybackRunContext? _terminatingRun;
    private int _seekInvocationCount;

    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _controlGate = new(1, 1);
    private readonly ManualResetEventSlim _pauseEvent = new(true);
    private readonly AutoResetEvent _controlWakeEvent = new(false);

    private bool _shutdownRequested;
    private bool _resourcesDisposed;

    public event EventHandler<PlaybackState>? StateChanged;
    public event EventHandler<PlaybackProgress>? ProgressChanged;
    public event EventHandler<int>? CountdownTick;
    public event EventHandler<IReadOnlyList<NoteEvent>>? ChordStarted;
    public event EventHandler<IReadOnlyList<NoteEvent>>? ChordPlayed;
    public event EventHandler<ChordPlaybackResult>? ChordEnded;
    public event EventHandler<Exception>? PlaybackError;

    internal long CurrentGeneration
    {
        get { lock (_stateLock) return _activeRun?.Generation ?? 0; }
    }

    internal bool HasActiveWorker
    {
        get
        {
            lock (_stateLock)
            {
                bool activeLive = _activeRun?.WorkerTask != null && !_activeRun.WorkerTask.IsCompleted;
                bool terminatingLive = _terminatingRun?.WorkerTask != null && !_terminatingRun.WorkerTask.IsCompleted;
                return activeLive || terminatingLive;
            }
        }
    }

    internal int LiveWorkerCount
    {
        get
        {
            lock (_stateLock)
            {
                int count = 0;
                if (_activeRun?.WorkerTask != null && !_activeRun.WorkerTask.IsCompleted) count++;
                if (_terminatingRun?.WorkerTask != null && !_terminatingRun.WorkerTask.IsCompleted) count++;
                return count;
            }
        }
    }

    internal Task? ActiveWorkerTask
    {
        get
        {
            lock (_stateLock)
            {
                return _activeRun?.WorkerTask;
            }
        }
    }

    internal Task? TerminatingWorkerTask
    {
        get
        {
            lock (_stateLock)
            {
                return _terminatingRun?.WorkerTask;
            }
        }
    }

    internal int SeekInvocationCount => _seekInvocationCount;

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
        PedalController? pedal = null,
        TimeSpan? workerTerminationTimeout = null)
    {
        _chordEngine = chordEngine;
        _keyState = keyState;
        _pedal = pedal ?? new PedalController(keyState.Backend);
        _workerTerminationTimeout = workerTerminationTimeout ?? TimeSpan.FromSeconds(2);
    }

    public void SetTimeline(MusicTimeline timeline)
    {
        SetTimelineAsync(timeline).GetAwaiter().GetResult();
    }

    public async Task SetTimelineAsync(MusicTimeline timeline, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await _controlGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await TerminateActiveWorkerInternalAsync(ct).ConfigureAwait(false);

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
        finally
        {
            _controlGate.Release();
        }
    }

    public void SetTrackFilter(Dictionary<int, bool>? trackFilter)
    {
        _trackFilter = trackFilter;
    }

    public void Play(double? startOffset = null)
    {
        PlayAsync(startOffset).GetAwaiter().GetResult();
    }

    public async Task PlayAsync(double? startOffset = null, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await _controlGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_timeline == null) return;

            bool isPaused;
            lock (_stateLock)
            {
                isPaused = (_state == PlaybackState.Paused);
            }

            if (isPaused && !startOffset.HasValue)
            {
                await ResumeInternalAsync(ct).ConfigureAwait(false);
                return;
            }

            double targetPos;
            lock (_stateLock)
            {
                if (startOffset.HasValue)
                {
                    targetPos = Math.Clamp(startOffset.Value, 0.0, _totalTime);
                }
                else if (_currentTime >= _totalTime && _totalTime > 0)
                {
                    targetPos = 0.0;
                }
                else
                {
                    targetPos = _currentTime;
                }
            }

            bool countdown = _countdownSeconds > 0;
            var targetState = countdown ? PlaybackState.Countdown : PlaybackState.Playing;
            await ReplaceWorkerAsync(targetPos, targetState, countdown, ct).ConfigureAwait(false);
        }
        finally
        {
            _controlGate.Release();
        }
    }

    public void Pause()
    {
        PauseAsync().GetAwaiter().GetResult();
    }

    public async Task PauseAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await _controlGate.WaitAsync(ct).ConfigureAwait(false);
        try
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
        finally
        {
            _controlGate.Release();
        }
    }

    public void Resume()
    {
        ResumeAsync().GetAwaiter().GetResult();
    }

    public async Task ResumeAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await _controlGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await ResumeInternalAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _controlGate.Release();
        }
    }

    private async Task ResumeInternalAsync(CancellationToken ct = default)
    {
        PlaybackRunContext? currentRun;
        bool isPaused;
        double currentPos;

        lock (_stateLock)
        {
            isPaused = (_state == PlaybackState.Paused);
            currentRun = _activeRun;
            currentPos = _currentTime;
        }

        if (!isPaused) return;

        if (currentRun == null || currentRun.WorkerTask == null || currentRun.WorkerTask.IsCompleted)
        {
            // Worker was terminated (e.g. paused seek), start a new generation from _currentTime without countdown
            await ReplaceWorkerAsync(currentPos, PlaybackState.Playing, countdown: false, ct).ConfigureAwait(false);
        }
        else
        {
            lock (_stateLock)
            {
                SetState(PlaybackState.Playing);
                _pauseEvent.Set();
                _controlWakeEvent.Set();
            }
        }
    }

    public void TogglePlayPause()
    {
        TogglePlayPauseAsync().GetAwaiter().GetResult();
    }

    public async Task TogglePlayPauseAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        PlaybackState state;
        lock (_stateLock)
        {
            state = _state;
        }

        if (state == PlaybackState.Playing)
        {
            await PauseAsync(ct).ConfigureAwait(false);
        }
        else if (state == PlaybackState.Paused)
        {
            await ResumeAsync(ct).ConfigureAwait(false);
        }
        else
        {
            await PlayAsync(null, ct).ConfigureAwait(false);
        }
    }

    public void Stop()
    {
        StopAsync().GetAwaiter().GetResult();
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _controlGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await TerminateActiveWorkerInternalAsync(ct).ConfigureAwait(false);

            lock (_stateLock)
            {
                _currentTime = 0.0;
                SetState(PlaybackState.Stopped);
                NotifyProgress();
            }
        }
        finally
        {
            _controlGate.Release();
        }
    }

    public void Seek(double targetSeconds)
    {
        Interlocked.Increment(ref _seekInvocationCount);
        SeekAsync(targetSeconds).GetAwaiter().GetResult();
    }

    public async Task SeekAsync(double targetSeconds, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await _controlGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            double clamped;
            bool wasPlaying;
            bool wasPaused;

            lock (_stateLock)
            {
                clamped = Math.Clamp(targetSeconds, 0.0, _totalTime);
                wasPlaying = (_state == PlaybackState.Playing);
                wasPaused = (_state == PlaybackState.Paused);
            }

            if (wasPlaying)
            {
                await ReplaceWorkerAsync(clamped, PlaybackState.Playing, countdown: false, ct).ConfigureAwait(false);
            }
            else if (wasPaused)
            {
                // Fully terminate old worker, reset position, remain Paused
                await TerminateActiveWorkerInternalAsync(ct).ConfigureAwait(false);

                lock (_stateLock)
                {
                    _currentTime = clamped;
                    SetState(PlaybackState.Paused);
                    NotifyProgress();
                }
            }
            else
            {
                lock (_stateLock)
                {
                    _currentTime = clamped;
                    if (_state == PlaybackState.Completed)
                    {
                        SetState(PlaybackState.Stopped);
                    }
                    NotifyProgress();
                }
            }
        }
        finally
        {
            _controlGate.Release();
        }
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

    private async Task TerminateActiveWorkerInternalAsync(CancellationToken ct = default)
    {
        PlaybackRunContext? oldRun;
        lock (_stateLock)
        {
            oldRun = _activeRun;
            _activeRun = null;
            if (oldRun != null)
            {
                _terminatingRun = oldRun;
            }
        }

        if (oldRun != null)
        {
            try
            {
                oldRun.Cts.Cancel();
            }
            catch { }

            _controlWakeEvent.Set();
            _pauseEvent.Set();

            if (oldRun.WorkerTask != null)
            {
                try
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var completed = await Task.WhenAny(oldRun.WorkerTask, Task.Delay(_workerTerminationTimeout, timeoutCts.Token)).ConfigureAwait(false);
                    if (completed != oldRun.WorkerTask)
                    {
                        _keyState.ReleaseAll();
                        _pedal.Release();
                        throw new TimeoutException($"Playback worker failed to terminate within {_workerTerminationTimeout.TotalMilliseconds}ms bounded timeout.");
                    }
                    timeoutCts.Cancel();
                    await oldRun.WorkerTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (oldRun.Cts.IsCancellationRequested)
                {
                }
            }

            _keyState.ReleaseAll();
            _pedal.Release();

            lock (_stateLock)
            {
                if (ReferenceEquals(_terminatingRun, oldRun))
                {
                    _terminatingRun = null;
                }
            }

            try
            {
                oldRun.Cts.Dispose();
            }
            catch { }
        }
    }

    private async Task ReplaceWorkerAsync(
        double startTime,
        PlaybackState targetState,
        bool countdown,
        CancellationToken externalCt = default)
    {
        // 1. Cancel and await old worker to fully exit before starting new worker
        await TerminateActiveWorkerInternalAsync(externalCt).ConfigureAwait(false);

        ThrowIfDisposed();

        // 2. Create and start new generation atomically
        PlaybackRunContext newRun;
        lock (_stateLock)
        {
            long generation = Interlocked.Increment(ref _generationCounter);
            newRun = new PlaybackRunContext(generation);
            _activeRun = newRun;

            _currentTime = startTime;
            _songAnchorSeconds = startTime;
            _perfAnchorTicks = Stopwatch.GetTimestamp();
            _activeSpeed = _speed;

            _pauseEvent.Set();
            _controlWakeEvent.Reset();

            SetState(targetState);
            NotifyProgress();

            if (targetState is PlaybackState.Playing or PlaybackState.Countdown)
            {
                newRun.WorkerTask = Task.Run(() => WorkerLoop(newRun, countdown));
            }
        }
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

    private bool IsCurrentGeneration(PlaybackRunContext run)
    {
        if (_shutdownRequested || _resourcesDisposed || run.Cts.IsCancellationRequested) return false;
        lock (_stateLock)
        {
            return _activeRun != null && _activeRun.Generation == run.Generation;
        }
    }

    private void WorkerLoop(PlaybackRunContext run, bool withCountdown)
    {
        try
        {
            // 1. Countdown Phase
            if (withCountdown && _countdownSeconds > 0)
            {
                for (int sec = _countdownSeconds; sec > 0; sec--)
                {
                    if (!IsCurrentGeneration(run)) return;
                    CountdownTick?.Invoke(this, sec);

                    long targetCountdownTicks = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
                    if (!PreciseWait(targetCountdownTicks, run))
                    {
                        return;
                    }
                }
            }

            if (!IsCurrentGeneration(run)) return;

            lock (_stateLock)
            {
                if (!IsCurrentGeneration(run)) return;
                SetState(PlaybackState.Playing);
            }

            if (_timeline == null) return;

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

            // Order by timestamp, pedal events before chords on ties
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
                    if (IsCurrentGeneration(run))
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
                if (!IsCurrentGeneration(run)) return;
                _songAnchorSeconds = _currentTime;
                _perfAnchorTicks = Stopwatch.GetTimestamp();
                _activeSpeed = _speed;
            }

            long lastProgressReportTicks = 0;

            for (int i = 0; i < items.Count; i++)
            {
                if (!IsCurrentGeneration(run)) break;

                var item = items[i];

                if (!WaitForNextEvent(item.Time, ref lastProgressReportTicks, run))
                {
                    break;
                }

                if (!IsCurrentGeneration(run)) break;

                // Execute event
                if (item.Type == EventType.Chord && item.Chord != null)
                {
                    ChordStarted?.Invoke(this, item.Chord.Notes);

                    try
                    {
                        var result = _chordEngine.PlayChordNotes(item.Chord.Notes, transpose: _transpose, ct: run.Cts.Token);

                        if (!IsCurrentGeneration(run)) break;

                        ChordPlayed?.Invoke(this, item.Chord.Notes);
                        Interlocked.Add(ref _playedNotes, result.PlayedCount);
                        Interlocked.Add(ref _skippedNotes, result.SkippedUnmappedCount + result.SkippedConflictCount);

                        ChordEnded?.Invoke(this, result);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
                else if (item.Type == EventType.Pedal && item.Pedal != null)
                {
                    if (!IsCurrentGeneration(run)) break;

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
                if (IsCurrentGeneration(run))
                {
                    _currentTime = _totalTime;
                    NotifyProgress();
                    SetState(PlaybackState.Completed);
                }
            }
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
            {
                lock (_stateLock)
                {
                    if (IsCurrentGeneration(run))
                    {
                        SetState(PlaybackState.Stopped);
                        PlaybackError?.Invoke(this, ex);
                    }
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
        PlaybackRunContext run)
    {
        while (true)
        {
            if (!IsCurrentGeneration(run)) return false;

            // Handle paused state
            if (!_pauseEvent.IsSet)
            {
                _keyState.ReleaseAll();
                _pedal.Release();
                long pauseStart = Stopwatch.GetTimestamp();

                try
                {
                    _pauseEvent.Wait(run.Cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }

                if (!IsCurrentGeneration(run)) return false;

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
                if (!IsCurrentGeneration(run)) return false;
                _currentTime = Math.Clamp(currentSongTime, 0.0, Math.Max(itemTargetTime, _totalTime));
            }

            // Report progress at ~40 Hz
            if (now - lastProgressReportTicks > (Stopwatch.Frequency / 40))
            {
                if (IsCurrentGeneration(run))
                {
                    NotifyProgress();
                }
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
                    if (!IsCurrentGeneration(run)) return false;
                    Thread.Yield();
                }
                break;
            }
        }

        return true;
    }

    private bool PreciseWait(long targetTicks, PlaybackRunContext run)
    {
        while (true)
        {
            if (!IsCurrentGeneration(run)) return false;

            long now = Stopwatch.GetTimestamp();
            long remainingTicks = targetTicks - now;

            if (remainingTicks <= 0) break;

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
                    if (!IsCurrentGeneration(run)) return false;
                    Thread.Yield();
                }
                break;
            }
        }

        return true;
    }

    private void ThrowIfDisposed()
    {
        if (_shutdownRequested || _resourcesDisposed)
        {
            throw new ObjectDisposedException(nameof(PlaybackScheduler));
        }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (_resourcesDisposed) return;
        _shutdownRequested = true;

        await _controlGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await TerminateActiveWorkerInternalAsync().ConfigureAwait(false);

            lock (_stateLock)
            {
                _currentTime = 0.0;
                SetState(PlaybackState.Stopped);
                NotifyProgress();
            }

            _resourcesDisposed = true;
            _keyState.Dispose();
            _pedal.Dispose();
            _controlWakeEvent.Dispose();
            _pauseEvent.Dispose();
        }
        catch (TimeoutException)
        {
            _keyState.ReleaseAll();
            _pedal.Release();
            throw;
        }
        finally
        {
            if (_resourcesDisposed)
            {
                _controlGate.Dispose();
            }
            else
            {
                _controlGate.Release();
            }
        }
    }
}
