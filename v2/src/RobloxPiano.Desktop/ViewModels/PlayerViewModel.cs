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
using RobloxPiano.Playback.Windows.WindowsIntegration;

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
    private readonly IRobloxTargetWindowService _targetService;
    private readonly IPlaybackTargetGuard _targetGuard;
    private readonly OverlayViewModel _overlayViewModel;

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
    private ObservableCollection<string> _availablePianoProfiles = new() { "Roblox 88키 (기본)", "Roblox 61키" };

    [ObservableProperty]
    private string _selectedPianoProfile = "Roblox 88키 (기본)";

    [ObservableProperty]
    private double _keyboardCanvasWidth = 936.0;

    [ObservableProperty]
    private bool _hasScore;

    [ObservableProperty]
    private ObservableCollection<PianoRollNoteViewModel> _pianoRollNotes = new();

    [ObservableProperty]
    private ObservableCollection<PianoKeyViewModel> _pianoKeys = new();

    [ObservableProperty]
    private ObservableCollection<RobloxWindowInfo> _availableRobloxWindows = new();

    [ObservableProperty]
    private RobloxWindowInfo? _selectedRobloxWindow;

    [ObservableProperty]
    private bool _isRobloxConnected;

    [ObservableProperty]
    private string _targetStatusText = "Roblox 대기 중";

    public bool IsRealInputBackend => _backend is ITargetedPlaybackBackend;
    public PlaybackScheduler Scheduler => _scheduler;
    public RobloxPianoMapper Mapper => _mapper;
    public PianoProfile CurrentPianoProfile => _mapper.Profile;
    public IRobloxTargetWindowService TargetService => _targetService;
    public IPlaybackTargetGuard TargetGuard => _targetGuard;
    public OverlayViewModel OverlayViewModel => _overlayViewModel;

    public PlayerViewModel() : this(new WindowsSendInputBackend())
    {
    }

    public PlayerViewModel(
        IPlaybackBackend backend,
        IRobloxTargetWindowService? targetService = null,
        IPlaybackTargetGuard? targetGuard = null)
    {
        _backend = backend;
        _keyState = new KeyStateManager(_backend, idleTimeoutSeconds: 2.0, enableWatchdog: true);
        _mapper = new RobloxPianoMapper();
        _chordEngine = new ChordEngine(_keyState, _mapper);
        _pedal = new PedalController(_backend);
        _scheduler = new PlaybackScheduler(_chordEngine, _keyState, _pedal);

        _targetService = targetService ?? new RobloxTargetWindowService();
        _targetGuard = targetGuard ?? new PlaybackTargetGuard(_targetService);
        _overlayViewModel = new OverlayViewModel(_scheduler);

        _scheduler.StateChanged += OnSchedulerStateChanged;
        _scheduler.ProgressChanged += OnSchedulerProgressChanged;
        _scheduler.CountdownTick += OnSchedulerCountdownTick;
        _scheduler.ChordStarted += OnSchedulerChordStarted;
        _scheduler.ChordEnded += OnSchedulerChordEnded;
        _scheduler.PlaybackError += OnSchedulerPlaybackError;

        _targetService.TargetChanged += OnTargetChanged;
        _targetService.AvailableTargetsChanged += OnAvailableTargetsChanged;

        InitializePianoKeys(_mapper.Profile);
        RefreshRobloxWindows();
    }

    partial void OnSelectedPianoProfileChanged(string value)
    {
        if (_disposed) return;

        // 1. Safety: stop active playback before switching profile
        if (_scheduler.State is PlaybackState.Playing or PlaybackState.Paused or PlaybackState.Countdown)
        {
            _scheduler.Stop();
            _backend.ReleaseAll();
            _keyState.ReleaseAll();
        }

        // 2. Load target profile
        var newProfile = value.Contains("61")
            ? PianoProfileLoader.Load61KeyProfile()
            : PianoProfileLoader.Load88KeyProfile();

        // 3. Update existing mapper instance
        _mapper.SetProfile(newProfile);

        // 4. Rebuild visible piano keyboard
        InitializePianoKeys(newProfile);

        // 5. Refresh piano roll if timeline is loaded
        if (CurrentTimeline != null)
        {
            RefreshPianoRoll(CurrentTimeline);
        }
    }

    public void InitializePianoKeys(PianoProfile profile)
    {
        var whiteNotes = new[] { 0, 2, 4, 5, 7, 9, 11 }; // C, D, E, F, G, A, B
        var keysList = new List<PianoKeyViewModel>();
        _keyLookup.Clear();

        int minPitch = profile.MinPitch;
        int maxPitch = profile.MaxPitch;

        // 1. Pass: Generate White keys
        int whiteCount = 0;
        for (int pitch = minPitch; pitch <= maxPitch; pitch++)
        {
            if (!profile.Keys.ContainsKey(pitch)) continue;

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

        // 2. Pass: Generate Black keys positioned relative to preceding white keys
        for (int pitch = minPitch; pitch <= maxPitch; pitch++)
        {
            if (!profile.Keys.ContainsKey(pitch)) continue;

            int noteInOctave = pitch % 12;
            bool isWhite = whiteNotes.Contains(noteInOctave);

            if (!isWhite)
            {
                int precedingWhiteCount = 0;
                for (int p = minPitch; p < pitch; p++)
                {
                    if (profile.Keys.ContainsKey(p) && whiteNotes.Contains(p % 12))
                    {
                        precedingWhiteCount++;
                    }
                }

                double left = (precedingWhiteCount * 18.0) - 6.0;
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

        KeyboardCanvasWidth = Math.Max(648.0, whiteCount * 18.0);
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

        // Build real timeline-backed piano roll notes
        RefreshPianoRoll(timeline);
        ResetKeyboardHighlight();

        HasScore = true;
        StatusText = "준비됨";

        _overlayViewModel.UpdateScoreTitle(Title);
        _scheduler.SetTimeline(timeline);
    }

    private void RefreshPianoRoll(MusicTimeline timeline)
    {
        var rollList = new List<PianoRollNoteViewModel>();
        int count = 0;
        int maxPitch = _mapper.MaxPitch;
        int minPitch = _mapper.MinPitch;
        int pitchSpan = Math.Max(1, maxPitch - minPitch);

        foreach (var note in timeline.Notes)
        {
            if (count++ >= 2000) break;

            double left = note.StartTime * PixelsPerSecond;
            double width = Math.Max(6.0, note.Duration * PixelsPerSecond);
            double top = Math.Clamp((maxPitch - note.Pitch) * (230.0 / pitchSpan) + 10.0, 5.0, 240.0);
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
    }

    partial void OnSelectedSpeedChanged(double value)
    {
        if (_disposed) return;
        _scheduler.Speed = value;
        _overlayViewModel.UpdateSpeed(value);
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
    public void RefreshRobloxWindows()
    {
        ThrowIfDisposed();
        _targetService.Refresh();
    }

    [RelayCommand]
    public void SelectRobloxWindow(RobloxWindowInfo? window)
    {
        ThrowIfDisposed();
        if (window != null)
        {
            _targetService.SetTarget(window.Hwnd);
        }
        else
        {
            _targetService.ClearTarget();
        }
    }

    private void OnTargetChanged(object? sender, EventArgs e)
    {
        PostToDispatcherOrDirect(() =>
        {
            SelectedRobloxWindow = _targetService.CurrentTarget;
            IsRobloxConnected = _targetService.HasTarget;
            UpdateTargetStatusText();
        });
    }

    private void OnAvailableTargetsChanged(object? sender, EventArgs e)
    {
        PostToDispatcherOrDirect(() =>
        {
            AvailableRobloxWindows = new ObservableCollection<RobloxWindowInfo>(_targetService.AvailableTargets);
            SelectedRobloxWindow = _targetService.CurrentTarget;
            IsRobloxConnected = _targetService.HasTarget;
            UpdateTargetStatusText();
        });
    }

    private void UpdateTargetStatusText()
    {
        if (_targetService.CurrentTarget != null && _targetService.HasTarget)
        {
            TargetStatusText = $"● {_targetService.CurrentTarget.DisplayName}";
        }
        else if (_targetService.AvailableTargets.Count > 1)
        {
            TargetStatusText = $"○ Roblox 창 {_targetService.AvailableTargets.Count}개 발견 (선택 필요)";
        }
        else
        {
            TargetStatusText = "○ Roblox 창 없음";
        }
    }

    [RelayCommand]
    public async Task TogglePlayPauseAsync()
    {
        ThrowIfDisposed();
        if (!HasScore) return;

        if (_scheduler.State == PlaybackState.Playing)
        {
            Pause();
        }
        else if (_scheduler.State == PlaybackState.Paused)
        {
            await ResumeOrPlayAsync();
        }
        else
        {
            await PlayAsync();
        }
    }

    public void TogglePlayPause()
    {
        TogglePlayPauseAsync().GetAwaiter().GetResult();
    }

    [RelayCommand]
    public async Task PlayAsync()
    {
        ThrowIfDisposed();
        if (!HasScore) return;

        if (IsRealInputBackend)
        {
            if (!_targetGuard.ValidateTarget())
            {
                StatusText = _targetService.AvailableTargets.Count > 1
                    ? "Roblox 창이 여러 개 열려 있습니다. 대상을 선택하세요."
                    : "Roblox 창을 찾을 수 없습니다.";
                return;
            }

            bool activated = await _targetGuard.ActivateAndVerifyTargetAsync();
            if (!activated)
            {
                StatusText = "Roblox 창으로 전환할 수 없습니다.";
                return;
            }

            _targetGuard.StartMonitoring(() =>
            {
                PostToDispatcherOrDirect(() =>
                {
                    _scheduler.Stop();
                    StatusText = "Roblox 창 포커스가 해제되어 재생을 중지했습니다.";
                });
            });
        }

        _scheduler.Play();
    }

    public void Play()
    {
        PlayAsync().GetAwaiter().GetResult();
    }

    private async Task ResumeOrPlayAsync()
    {
        if (IsRealInputBackend)
        {
            if (!_targetGuard.ValidateTarget())
            {
                StatusText = "Roblox 창을 찾을 수 없습니다.";
                return;
            }

            bool activated = await _targetGuard.ActivateAndVerifyTargetAsync();
            if (!activated)
            {
                StatusText = "Roblox 창으로 전환할 수 없습니다.";
                return;
            }

            _targetGuard.StartMonitoring(() =>
            {
                PostToDispatcherOrDirect(() =>
                {
                    _scheduler.Stop();
                    StatusText = "Roblox 창 포커스가 해제되어 재생을 중지했습니다.";
                });
            });
        }

        _scheduler.Resume();
    }

    [RelayCommand]
    public void Pause()
    {
        ThrowIfDisposed();
        _targetGuard.StopMonitoring();
        _scheduler.Pause();
    }

    [RelayCommand]
    public void Stop()
    {
        ThrowIfDisposed();
        _targetGuard.StopMonitoring();
        _scheduler.Stop();
    }

    [RelayCommand]
    public void Seek(double targetSeconds)
    {
        ThrowIfDisposed();
        _scheduler.Seek(targetSeconds);
        PlayheadCanvasLeft = targetSeconds * PixelsPerSecond;
    }

    public async Task HandleHotkeyPlayAsync()
    {
        if (_disposed) return;
        if (!HasScore)
        {
            StatusText = "재생할 악보를 먼저 선택하세요.";
            return;
        }

        if (_scheduler.State == PlaybackState.Playing)
        {
            return;
        }

        if (_scheduler.State == PlaybackState.Paused)
        {
            await ResumeOrPlayAsync();
        }
        else
        {
            await PlayAsync();
        }
    }

    public async Task HandleHotkeyPauseResumeAsync()
    {
        if (_disposed) return;
        if (!HasScore) return;

        if (_scheduler.State == PlaybackState.Playing)
        {
            Pause();
        }
        else if (_scheduler.State == PlaybackState.Paused)
        {
            await ResumeOrPlayAsync();
        }
    }

    public void HandleHotkeyStop()
    {
        if (_disposed) return;
        Stop();
    }

    private static void PostToDispatcherOrDirect(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.InvokeAsync(action);
        }
        else
        {
            action();
        }
    }

    private void OnSchedulerStateChanged(object? sender, PlaybackState state)
    {
        if (state is PlaybackState.Stopped or PlaybackState.Completed)
        {
            _targetGuard.StopMonitoring();
        }

        PostToDispatcherOrDirect(() =>
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
        PostToDispatcherOrDirect(() =>
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
        PostToDispatcherOrDirect(() =>
        {
            CountdownValue = tick;
            StatusText = $"{tick}초 후 시작";
        });
    }

    private void OnSchedulerChordStarted(object? sender, IReadOnlyList<NoteEvent> notes)
    {
        PostToDispatcherOrDirect(() =>
        {
            HighlightNotes(notes);
        });
    }

    private void OnSchedulerChordEnded(object? sender, ChordPlaybackResult result)
    {
        PostToDispatcherOrDirect(() =>
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
        _targetGuard.StopMonitoring();

        PostToDispatcherOrDirect(() =>
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

        _targetGuard.StopMonitoring();

        _targetService.TargetChanged -= OnTargetChanged;
        _targetService.AvailableTargetsChanged -= OnAvailableTargetsChanged;

        _scheduler.StateChanged -= OnSchedulerStateChanged;
        _scheduler.ProgressChanged -= OnSchedulerProgressChanged;
        _scheduler.CountdownTick -= OnSchedulerCountdownTick;
        _scheduler.ChordStarted -= OnSchedulerChordStarted;
        _scheduler.ChordEnded -= OnSchedulerChordEnded;
        _scheduler.PlaybackError -= OnSchedulerPlaybackError;

        _targetGuard.Dispose();
        _overlayViewModel.Dispose();
        _scheduler.Dispose();
        _backend.Dispose();
    }
}
