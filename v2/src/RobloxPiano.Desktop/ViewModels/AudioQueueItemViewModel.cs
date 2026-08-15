using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using RobloxPiano.Core.Audio;

namespace RobloxPiano.Desktop.ViewModels;

public enum AudioItemStatus
{
    Pending,
    Probing,
    Converting,
    Prepared,
    Failed,
    Cancelled
}

public partial class AudioQueueItemViewModel : ObservableObject
{
    public string FilePath { get; }
    public string FileName { get; }
    public string JobId { get; }

    [ObservableProperty]
    private string _sourceType = "-";

    [ObservableProperty]
    private AudioItemStatus _status = AudioItemStatus.Pending;

    [ObservableProperty]
    private string _statusText = "대기";

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _durationText = "-";

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private bool _isPrepared;

    [ObservableProperty]
    private bool _isFailed;

    [ObservableProperty]
    private bool _isProcessing;

    public AudioIngestResult? Result { get; set; }
    public string? NormalizedAudioPath => Result?.NormalizedAudioPath;

    public AudioQueueItemViewModel(string filePath, string? jobId = null)
    {
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
        JobId = jobId ?? Guid.NewGuid().ToString("N");
        var extType = AudioSourceTypeExtensions.FromExtension(filePath);
        SourceType = extType.ToFriendlyString();
    }

    public void SetProbing()
    {
        Status = AudioItemStatus.Probing;
        StatusText = "검사 중";
        IsProcessing = true;
        IsPrepared = false;
        IsFailed = false;
        ErrorMessage = string.Empty;
    }

    public void SetConverting(double progressFraction)
    {
        Status = AudioItemStatus.Converting;
        StatusText = "변환 중";
        IsProcessing = true;
        IsPrepared = false;
        IsFailed = false;
        ProgressPercent = Math.Clamp(progressFraction * 100.0, 0.0, 100.0);
    }

    public void SetPrepared(AudioIngestResult result)
    {
        Result = result;
        Status = AudioItemStatus.Prepared;
        StatusText = "준비 완료";
        IsProcessing = false;
        IsPrepared = true;
        IsFailed = false;
        ProgressPercent = 100.0;

        if (result.Metadata != null)
        {
            var ts = TimeSpan.FromSeconds(result.Metadata.DurationSeconds);
            DurationText = $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
            SourceType = result.Metadata.CodecName.ToUpperInvariant();
        }
    }

    public void SetFailed(string error)
    {
        Status = AudioItemStatus.Failed;
        StatusText = "실패";
        IsProcessing = false;
        IsPrepared = false;
        IsFailed = true;
        ErrorMessage = error;
    }

    public void SetCancelled()
    {
        Status = AudioItemStatus.Cancelled;
        StatusText = "취소됨";
        IsProcessing = false;
        IsPrepared = false;
        IsFailed = false;
        ErrorMessage = "사용자에 의해 취소됨";
    }
}
