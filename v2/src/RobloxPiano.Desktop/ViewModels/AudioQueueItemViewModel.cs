using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using RobloxPiano.Core.Audio;
using RobloxPiano.Core.Transcription;

namespace RobloxPiano.Desktop.ViewModels;

public enum AudioItemStatus
{
    Pending,
    Probing,
    Converting,
    Prepared,
    AiTranscribing,
    AiCompleted,
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

    [ObservableProperty]
    private bool _isAiProcessing;

    [ObservableProperty]
    private bool _isAiCompleted;

    [ObservableProperty]
    private string _aiStatusText = "";

    [ObservableProperty]
    private string _noteStatsText = "";

    public AudioIngestResult? Result { get; set; }
    public string? NormalizedAudioPath => Result?.NormalizedAudioPath;

    public TranscriptionResult? AiResult { get; set; }
    public string? GeneratedMidiPath => AiResult?.GeneratedMidiPath;

    public bool CanStartAi => IsPrepared && !IsAiProcessing && !IsProcessing;
    public bool HasAiResult => IsAiCompleted && AiResult != null && AiResult.Success;

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
        OnPropertyChanged(nameof(CanStartAi));
    }

    public void SetConverting(double progressFraction)
    {
        Status = AudioItemStatus.Converting;
        StatusText = "변환 중";
        IsProcessing = true;
        IsPrepared = false;
        IsFailed = false;
        ProgressPercent = Math.Clamp(progressFraction * 100.0, 0.0, 100.0);
        OnPropertyChanged(nameof(CanStartAi));
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
        OnPropertyChanged(nameof(CanStartAi));
    }

    public void SetAiStarting(string msg = "AI 엔진 시작 중...")
    {
        Status = AudioItemStatus.AiTranscribing;
        StatusText = "AI 분석 중";
        AiStatusText = msg;
        IsProcessing = true;
        IsAiProcessing = true;
        IsFailed = false;
        ErrorMessage = string.Empty;
        OnPropertyChanged(nameof(CanStartAi));
    }

    public void SetAiAnalyzing(string msg = "오디오 분석 중...")
    {
        Status = AudioItemStatus.AiTranscribing;
        StatusText = "AI 분석 중";
        AiStatusText = msg;
        IsProcessing = true;
        IsAiProcessing = true;
        IsFailed = false;
        OnPropertyChanged(nameof(CanStartAi));
    }

    public void SetAiCompleted(TranscriptionResult result)
    {
        AiResult = result;
        Status = AudioItemStatus.AiCompleted;
        StatusText = "분석 완료";
        AiStatusText = "악보 생성 완료";
        IsProcessing = false;
        IsAiProcessing = false;
        IsAiCompleted = true;
        IsFailed = false;
        ErrorMessage = string.Empty;

        NoteStatsText = $"총 {result.NoteCount}음 (연주 가능 {result.PlayableNoteCount}음)";
        OnPropertyChanged(nameof(CanStartAi));
        OnPropertyChanged(nameof(HasAiResult));
    }

    public void UpdateDiagnostics(int playableNotes, int outOfRangeNotes)
    {
        if (AiResult != null)
        {
            NoteStatsText = $"총 {AiResult.NoteCount}음 (연주 가능 {playableNotes}음)";
        }
    }

    public void SetAiFailed(string error)
    {
        Status = AudioItemStatus.Failed;
        StatusText = "분석 실패";
        AiStatusText = "실패";
        IsProcessing = false;
        IsAiProcessing = false;
        IsFailed = true;
        ErrorMessage = error;
        OnPropertyChanged(nameof(CanStartAi));
        OnPropertyChanged(nameof(HasAiResult));
    }

    public void SetAiCancelled()
    {
        Status = AudioItemStatus.Cancelled;
        StatusText = "취소됨";
        AiStatusText = "취소됨";
        IsProcessing = false;
        IsAiProcessing = false;
        IsFailed = false;
        ErrorMessage = "사용자에 의해 취소됨";
        OnPropertyChanged(nameof(CanStartAi));
        OnPropertyChanged(nameof(HasAiResult));
    }

    public void SetFailed(string error)
    {
        Status = AudioItemStatus.Failed;
        StatusText = "실패";
        IsProcessing = false;
        IsPrepared = false;
        IsAiProcessing = false;
        IsFailed = true;
        ErrorMessage = error;
        OnPropertyChanged(nameof(CanStartAi));
        OnPropertyChanged(nameof(HasAiResult));
    }

    public void SetCancelled()
    {
        Status = AudioItemStatus.Cancelled;
        StatusText = "취소됨";
        IsProcessing = false;
        IsPrepared = false;
        IsAiProcessing = false;
        IsFailed = false;
        ErrorMessage = "사용자에 의해 취소됨";
        OnPropertyChanged(nameof(CanStartAi));
        OnPropertyChanged(nameof(HasAiResult));
    }
}
