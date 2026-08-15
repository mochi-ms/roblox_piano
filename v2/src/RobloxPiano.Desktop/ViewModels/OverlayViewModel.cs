using CommunityToolkit.Mvvm.ComponentModel;
using RobloxPiano.Playback.Windows.Playback;

namespace RobloxPiano.Desktop.ViewModels;

public partial class OverlayViewModel : ObservableObject, IDisposable
{
    private readonly PlaybackScheduler _scheduler;
    private bool _disposed;

    [ObservableProperty]
    private string _title = "악보 없음";

    [ObservableProperty]
    private string _statusText = "정지됨";

    [ObservableProperty]
    private string _formattedCurrentTime = "00:00";

    [ObservableProperty]
    private string _formattedTotalTime = "00:00";

    [ObservableProperty]
    private string _formattedSpeed = "1.0x";

    [ObservableProperty]
    private string _hotkeyHint = "F6 재생 · F7 일시정지 · ESC 정지";

    [ObservableProperty]
    private bool _isVisible;

    public OverlayViewModel(PlaybackScheduler scheduler)
    {
        _scheduler = scheduler;
        _scheduler.StateChanged += OnSchedulerStateChanged;
        _scheduler.ProgressChanged += OnSchedulerProgressChanged;
    }

    public void UpdateScoreTitle(string title)
    {
        Title = string.IsNullOrWhiteSpace(title) ? "제목 없음" : title;
    }

    public void UpdateScoreInfo(string title, double totalTime)
    {
        Title = string.IsNullOrWhiteSpace(title) ? "제목 없음" : title;
        FormattedTotalTime = FormatDuration(totalTime);
    }

    public void UpdateSpeed(double speed)
    {
        FormattedSpeed = $"{speed:0.0}x";
    }

    private void OnSchedulerStateChanged(object? sender, PlaybackState state)
    {
        StatusText = state switch
        {
            PlaybackState.Playing => "재생 중",
            PlaybackState.Paused => "일시정지",
            PlaybackState.Countdown => "시작 대기",
            PlaybackState.Stopped => "정지됨",
            PlaybackState.Completed => "완료됨",
            _ => "준비됨"
        };

        // Show overlay automatically during active playback states
        IsVisible = state is PlaybackState.Playing or PlaybackState.Paused or PlaybackState.Countdown;
    }

    private void OnSchedulerProgressChanged(object? sender, PlaybackProgress prog)
    {
        FormattedCurrentTime = FormatDuration(prog.CurrentTime);
        FormattedTotalTime = FormatDuration(prog.TotalTime);
    }

    private static string FormatDuration(double seconds)
    {
        if (seconds <= 0) return "00:00";
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _scheduler.StateChanged -= OnSchedulerStateChanged;
        _scheduler.ProgressChanged -= OnSchedulerProgressChanged;
    }
}
