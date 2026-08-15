using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobloxPiano.Core.Importers;
using RobloxPiano.Core.Library;
using RobloxPiano.Core.Music;
using RobloxPiano.Core.Piano;
using RobloxPiano.Playback.Windows.Input;
using RobloxPiano.Playback.Windows.Playback;

namespace RobloxPiano.Desktop.ViewModels;

public partial class PlayerViewModel : ObservableObject, IDisposable
{
    private const double PixelsPerSecond = 80.0;

    private readonly PlaybackScheduler _scheduler;
    private readonly KeyStateManager _keyState;
    private readonly ChordEngine _chordEngine;
    private readonly PedalController _pedal;
    private readonly RobloxPianoMapper _mapper;
    private readonly IPlaybackBackend _backend;

    private readonly Dictionary<int, PianoKeyViewModel> _keyLookup = new();
    private bool _isUpdatingProgressFromScheduler;
    private bool _disposed;

    [ObservableProperty]
    private ScoreItem? _currentScore;

    [ObservableProperty]
    private MusicTimeline? _currentTimeline;

    [ObservableProperty]
    private string _title = "악보를 선택하세요";

    [ObservableProperty]
    private string _sourceType = "-";

    [ObservableProperty]
    private string _trackAndTempoInfo = "-";

    [ObservableProperty]
    private string _formattedDuration = "00:00";

    [ObservableProperty]
    private string _formattedBpm = "-";

    [ObservableProperty]
    private string _formattedTotalNotes = "-";

    [ObservableProperty]
    private string _pitchRangeText = "-";

    [ObservableProperty]
    private string _formattedCurrentTime = "00:00";

    [ObservableProperty]
    private string _formattedTotalTime = "/ 00:00";

    [ObservableProperty]
    private double _currentTime;

    [ObservableProperty]
    private double _totalTime;

    [ObservableProperty]
    private double _playheadCanvasLeft;

    [ObservableProperty]
    private double _pianoRollWidth = 1000;

    [ObservableProperty]
    private string _statusText = "준비됨";

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _isCountdown;

    [ObservableProperty]
    private int _countdownValue;

    [ObservableProperty]
    private double _selectedSpeed = 1.0;

    [ObservableProperty]
    private int _selectedTranspose = 0;

    [ObservableProperty]
    private bool _hasScore;

    [ObservableProperty]
    private ObservableCollection<PianoRollNoteViewModel> _pianoRollNotes = new();

    [ObservableProperty]
    private ObservableCollection<PianoKeyViewModel> _pianoKeys = new();

    public PlaybackScheduler Scheduler => _scheduler;

    public PlayerViewModel() : this(new WindowsSendInputBackend())
    {
    }

    public PlayerViewModel(IPlaybackBackend backend)
    {
        _backend = backend;
        _keyState = new KeyStateManager(_backend, idleTimeoutSeconds: 2.0, enableWatchdog: true);
        _mapper = new RobloxPianoMapper();
        _chordEngine = new ChordEngine(_keyState, _mapper);
        _pedal = new PedalController(_backend);
        _scheduler = new PlaybackScheduler(_chordEngine, _keyState, _pedal);

        _scheduler.StateChanged += OnSchedulerStateChanged;
        _scheduler.ProgressChanged += OnSchedulerProgressChanged;
        _scheduler.CountdownTick += OnSchedulerCountdownTick;
        _scheduler.ChordStarted += OnSchedulerChordStarted;
        _scheduler.ChordPlayed += OnSchedulerChordPlayed;
        _scheduler.ChordEnded += OnSchedulerChordEnded;
        _scheduler.PlaybackError += OnSchedulerPlaybackError;

        Initialize61Keys();
    }

    private void Initialize61Keys()
    {
        // 61 Keys: C2 (36) to C7 (96)
        // 36 White keys (width 18px each -> Total 648px)
        // 25 Black keys (width 11px each, height 34px)
        int whiteCount = 0;
        var whiteNotes = new[] { 0, 2, 4, 5, 7, 9, 11 }; // C, D, E, F, G, A, B

        var keysList = new List<PianoKeyViewModel>();

        // Generate 61 keys
        for (int pitch = 36; pitch <= 96; pitch++)
        {
            int noteInOctave = pitch % 12;
            bool isWhite = whiteNotes.Contains(noteInOctave);

            if (isWhite)
            {
                double left = whiteCount * 18.0;
                string noteName = FormatPitch(pitch);
                var keyVm = new PianoKeyViewModel
                {
                    Pitch = pitch,
                    NoteName = noteName,
                    IsBlack = false,
                    KeyLeft = left,
                    KeyWidth = 17.0,
                    KeyHeight = 56.0,
                    ZIndex = 1
                };
                keysList.Add(keyVm);
                _keyLookup[pitch] = keyVm;
                whiteCount++;
            }
        }

        // Generate Black keys positioned relative to white keys
        whiteCount = 0;
        for (int pitch = 36; pitch <= 96; pitch++)
        {
            int noteInOctave = pitch % 12;
            bool isWhite = whiteNotes.Contains(noteInOctave);

            if (isWhite)
            {
                whiteCount++;
            }
            else
            {
                // Black key position: between previous white key and current
                double left = (whiteCount * 18.0) - 6.0;
                string noteName = FormatPitch(pitch);
                var keyVm = new PianoKeyViewModel
                {
                    Pitch = pitch,
                    NoteName = noteName,
                    IsBlack = true,
                    KeyLeft = left,
                    KeyWidth = 11.0,
                    KeyHeight = 34.0,
                    ZIndex = 2
                };
                keysList.Add(keyVm);
                _keyLookup[pitch] = keyVm;
            }
        }

        PianoKeys = new ObservableCollection<PianoKeyViewModel>(keysList.OrderBy(k => k.ZIndex).ThenBy(k => k.Pitch));
    }

    public async Task LoadScoreAsync(ScoreItem score, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (score == null) return;

        if (string.IsNullOrEmpty(score.FilePath) || !File.Exists(score.FilePath))
        {
            StatusText = "악보 파일을 찾을 수 없습니다.";
            HasScore = false;
            return;
        }

        try
        {
            MusicTimeline timeline;
            string ext = Path.GetExtension(score.FilePath).ToLowerInvariant();

            if (ext is ".mid" or ".midi" || score.SourceType == "MIDI")
            {
                var importer = new MidiImporter();
                timeline = importer.ImportScore(score.FilePath);
            }
            else if (ext is ".mml" or ".txt" || score.SourceType == "MML")
            {
                var importer = new MmlImporter();
                timeline = importer.ImportScore(score.FilePath);
            }
            else
            {
                StatusText = "지원하지 않는 악보 형식입니다.";
                HasScore = false;
                return;
            }

            CurrentScore = score;
            LoadTimeline(timeline, score.Title, score.SourceType);
        }
        catch (Exception ex)
        {
            StatusText = $"악보 로드 실패: {ex.Message}";
            HasScore = false;
        }
    }

    public void LoadTimeline(MusicTimeline timeline, string title = "제목 없음", string sourceType = "MIDI")
    {
        ThrowIfDisposed();
        CurrentTimeline = timeline;
        Title = !string.IsNullOrEmpty(title) && title != "Untitled" && title != "MML Score"
            ? title
            : (!string.IsNullOrEmpty(timeline.Title) && timeline.Title != "Untitled" && timeline.Title != "MML Score" ? timeline.Title : "제목 없음");
        SourceType = sourceType;

        // Calculate metadata
        int trackCount = timeline.TrackNames.Count > 0 ? timeline.TrackNames.Count : 1;
        var (num, den) = timeline.TimeSignature;
        TrackAndTempoInfo = $"{trackCount}트랙 · {num}/{den}박자";

        FormattedDuration = FormatDuration(timeline.Duration);
        FormattedTotalTime = $"/ {FormattedDuration}";
        FormattedCurrentTime = "00:00";
        CurrentTime = 0.0;
        TotalTime = timeline.Duration;
        PlayheadCanvasLeft = 0.0;
        PianoRollWidth = Math.Max(1000.0, timeline.Duration * PixelsPerSecond + 100.0);

        FormattedBpm = $"{Math.Round(timeline.InitialBpm)}";
        FormattedTotalNotes = $"{timeline.Notes.Count:N0}";

        var (minPitch, maxPitch) = timeline.PitchRange;
        if (minPitch <= maxPitch && minPitch > 0)
        {
            PitchRangeText = $"{FormatPitch(minPitch)} – {FormatPitch(maxPitch)}";
        }
        else
        {
            PitchRangeText = "-";
        }

        // Build real timeline-backed piano roll notes (cap to 2000 for high performance)
        var rollList = new List<PianoRollNoteViewModel>();
        int count = 0;
        foreach (var note in timeline.Notes)
        {
            if (count++ >= 2000) break;

            double left = note.StartTime * PixelsPerSecond;
            double width = Math.Max(6.0, note.Duration * PixelsPerSecond);
            // Pitch 96 (C7) at top 10px, Pitch 36 (C2) at bottom 240px
            double top = Math.Clamp((96 - note.Pitch) * 3.8 + 10.0, 5.0, 240.0);
            string brushKey = note.Hand == HandType.Left ? "#34D399" : "#5B8DEF";

            rollList.Add(new PianoRollNoteViewModel(
                Pitch: note.Pitch,
                StartTime: note.StartTime,
                Duration: note.Duration,
                Hand: note.Hand,
                CanvasLeft: left,
                CanvasTop: top,
                Width: width,
                Height: 6.0,
                ColorBrushKey: brushKey
            ));
        }

        PianoRollNotes = new ObservableCollection<PianoRollNoteViewModel>(rollList);
        ResetKeyboardHighlight();

        HasScore = true;
        StatusText = "준비됨";

        _scheduler.SetTimeline(timeline);
    }

    partial void OnSelectedSpeedChanged(double value)
    {
        if (_disposed) return;
        _scheduler.Speed = value;
    }

    partial void OnSelectedTransposeChanged(int value)
    {
        if (_disposed) return;
        _scheduler.Transpose = value;
    }

    partial void OnCurrentTimeChanged(double value)
    {
        if (_isUpdatingProgressFromScheduler || _disposed) return;

        // When user manually drags slider or sets position
        if (!_isPlaying && !_isCountdown)
        {
            _scheduler.Seek(value);
            PlayheadCanvasLeft = value * PixelsPerSecond;
        }
    }

    [RelayCommand]
    public void TogglePlayPause()
    {
        ThrowIfDisposed();
        if (!HasScore) return;
        _scheduler.TogglePlayPause();
    }

    [RelayCommand]
    public void Play()
    {
        ThrowIfDisposed();
        if (!HasScore) return;
        _scheduler.Play();
    }

    [RelayCommand]
    public void Pause()
    {
        ThrowIfDisposed();
        _scheduler.Pause();
    }

    [RelayCommand]
    public void Stop()
    {
        ThrowIfDisposed();
        _scheduler.Stop();
    }

    [RelayCommand]
    public void Seek(double targetSeconds)
    {
        ThrowIfDisposed();
        _scheduler.Seek(targetSeconds);
        PlayheadCanvasLeft = targetSeconds * PixelsPerSecond;
    }

    private void OnSchedulerStateChanged(object? sender, PlaybackState state)
    {
        Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            IsPlaying = (state == PlaybackState.Playing);
            IsPaused = (state == PlaybackState.Paused);
            IsCountdown = (state == PlaybackState.Countdown);

            StatusText = state switch
            {
                PlaybackState.Idle => "준비됨",
                PlaybackState.Countdown => $"{CountdownValue}초 후 시작",
                PlaybackState.Playing => "재생 중",
                PlaybackState.Paused => "일시정지",
                PlaybackState.Stopped => "정지됨",
                PlaybackState.Completed => "재생 완료",
                _ => "준비됨"
            };

            if (state is PlaybackState.Stopped or PlaybackState.Completed)
            {
                ResetKeyboardHighlight();
            }
        });
    }

    private void OnSchedulerProgressChanged(object? sender, PlaybackProgress prog)
    {
        Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            _isUpdatingProgressFromScheduler = true;
            try
            {
                CurrentTime = prog.CurrentTime;
                PlayheadCanvasLeft = prog.CurrentTime * PixelsPerSecond;
                FormattedCurrentTime = FormatDuration(prog.CurrentTime);
                FormattedTotalTime = $"/ {FormatDuration(prog.TotalTime)}";
            }
            finally
            {
                _isUpdatingProgressFromScheduler = false;
            }
        });
    }

    private void OnSchedulerCountdownTick(object? sender, int tick)
    {
        Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            CountdownValue = tick;
            StatusText = $"{tick}초 후 시작";
        });
    }

    private void OnSchedulerChordStarted(object? sender, IReadOnlyList<NoteEvent> notes)
    {
        Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            HighlightNotes(notes);
        });
    }

    private void OnSchedulerChordPlayed(object? sender, IReadOnlyList<NoteEvent> notes)
    {
        Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            HighlightNotes(notes);
        });
    }

    private void OnSchedulerChordEnded(object? sender, ChordPlaybackResult result)
    {
        Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            ResetKeyboardHighlight();
        });
    }

    private void HighlightNotes(IReadOnlyList<NoteEvent> notes)
    {
        ResetKeyboardHighlight();
        foreach (var n in notes)
        {
            int p = n.Pitch + SelectedTranspose;
            if (_keyLookup.TryGetValue(p, out var keyVm))
            {
                keyVm.IsActive = true;
            }
        }
    }

    private void ResetKeyboardHighlight()
    {
        foreach (var key in PianoKeys)
        {
            key.IsActive = false;
        }
    }

    private void OnSchedulerPlaybackError(object? sender, Exception ex)
    {
        Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            StatusText = $"오류: {ex.Message}";
            IsPlaying = false;
            IsPaused = false;
            IsCountdown = false;
            ResetKeyboardHighlight();
        });
    }

    private static string FormatDuration(double seconds)
    {
        if (seconds <= 0) return "00:00";
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
    }

    private static string FormatPitch(int pitch)
    {
        string[] noteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        int octave = (pitch / 12) - 1;
        int noteIndex = pitch % 12;
        return $"{noteNames[noteIndex]}{octave}";
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PlayerViewModel));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _scheduler.StateChanged -= OnSchedulerStateChanged;
        _scheduler.ProgressChanged -= OnSchedulerProgressChanged;
        _scheduler.CountdownTick -= OnSchedulerCountdownTick;
        _scheduler.ChordStarted -= OnSchedulerChordStarted;
        _scheduler.ChordPlayed -= OnSchedulerChordPlayed;
        _scheduler.ChordEnded -= OnSchedulerChordEnded;
        _scheduler.PlaybackError -= OnSchedulerPlaybackError;

        _scheduler.Dispose();
        _backend.Dispose();
    }
}
