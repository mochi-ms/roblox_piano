using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobloxPiano.Core.Library;
using RobloxPiano.Core.Importers;
using RobloxPiano.Core.Music;
using RobloxPiano.Core.Piano;
using RobloxPiano.Playback.Windows.Input;
using RobloxPiano.Playback.Windows.Playback;

namespace RobloxPiano.Desktop.ViewModels;

public partial class PlayerViewModel : ObservableObject, IDisposable
{
    private readonly PlaybackScheduler _scheduler;
    private readonly KeyStateManager _keyState;
    private readonly ChordEngine _chordEngine;
    private readonly PedalController _pedal;
    private readonly RobloxPianoMapper _mapper;
    private readonly IPlaybackBackend _backend;

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
    private ObservableCollection<int> _activePitches = new();

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
        _scheduler.ChordPlayed += OnSchedulerChordPlayed;
        _scheduler.PlaybackError += OnSchedulerPlaybackError;
    }

    public async Task LoadScoreAsync(ScoreItem score, CancellationToken ct = default)
    {
        if (score == null) return;

        if (string.IsNullOrEmpty(score.FilePath) || !File.Exists(score.FilePath))
        {
            StatusText = "악보 파일을 찾을 수 없습니다.";
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
                return;
            }

            CurrentScore = score;
            LoadTimeline(timeline, score.Title, score.SourceType);
        }
        catch (Exception ex)
        {
            StatusText = $"악보 로드 실패: {ex.Message}";
        }
    }

    public void LoadTimeline(MusicTimeline timeline, string title = "제목 없음", string sourceType = "MIDI")
    {
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

        HasScore = true;
        StatusText = "준비됨";

        _scheduler.SetTimeline(timeline);
    }

    partial void OnSelectedSpeedChanged(double value)
    {
        _scheduler.Speed = value;
    }

    partial void OnSelectedTransposeChanged(int value)
    {
        _scheduler.Transpose = value;
    }

    partial void OnCurrentTimeChanged(double value)
    {
        // When user drags the slider, seek
        if (!_isPlaying && !_isCountdown)
        {
            _scheduler.Seek(value);
        }
    }

    [RelayCommand]
    public void TogglePlayPause()
    {
        if (!HasScore) return;
        _scheduler.TogglePlayPause();
    }

    [RelayCommand]
    public void Play()
    {
        if (!HasScore) return;
        _scheduler.Play();
    }

    [RelayCommand]
    public void Pause()
    {
        _scheduler.Pause();
    }

    [RelayCommand]
    public void Stop()
    {
        _scheduler.Stop();
    }

    [RelayCommand]
    public void Seek(double targetSeconds)
    {
        _scheduler.Seek(targetSeconds);
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
                ActivePitches.Clear();
            }
        });
    }

    private void OnSchedulerProgressChanged(object? sender, PlaybackProgress prog)
    {
        Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            CurrentTime = prog.CurrentTime;
            FormattedCurrentTime = FormatDuration(prog.CurrentTime);
            FormattedTotalTime = $"/ {FormatDuration(prog.TotalTime)}";
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

    private void OnSchedulerChordPlayed(object? sender, IReadOnlyList<NoteEvent> notes)
    {
        Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            ActivePitches.Clear();
            foreach (var n in notes)
            {
                ActivePitches.Add(n.Pitch + SelectedTranspose);
            }
        });
    }

    private void OnSchedulerPlaybackError(object? sender, Exception ex)
    {
        Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            StatusText = $"오류: {ex.Message}";
            IsPlaying = false;
            IsPaused = false;
            IsCountdown = false;
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

    public void Dispose()
    {
        _scheduler.Dispose();
        _keyState.Dispose();
        _pedal.Dispose();
        _backend.Dispose();
    }
}
