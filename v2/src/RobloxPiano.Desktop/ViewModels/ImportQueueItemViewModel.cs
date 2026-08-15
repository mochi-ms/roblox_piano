using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using RobloxPiano.Core.Importing;
using RobloxPiano.Core.Library;

namespace RobloxPiano.Desktop.ViewModels;

public enum ImportItemStatus
{
    Pending,
    Importing,
    Completed,
    Failed,
    Cancelled
}

public partial class ImportQueueItemViewModel : ObservableObject
{
    public string FilePath { get; }
    public string FileName { get; }

    [ObservableProperty]
    private string _sourceType = "-";

    [ObservableProperty]
    private ImportItemStatus _status = ImportItemStatus.Pending;

    [ObservableProperty]
    private string _statusText = "대기";

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _durationText = "-";

    [ObservableProperty]
    private string _notesText = "-";

    [ObservableProperty]
    private string _bpmText = "-";

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private bool _isFailed;

    [ObservableProperty]
    private bool _isImporting;

    public ImportResult? Result { get; set; }
    public ScoreItem? CreatedScore { get; set; }

    public ImportQueueItemViewModel(string filePath)
    {
        FilePath = filePath;
        FileName = System.IO.Path.GetFileName(filePath);
        var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
        _sourceType = ext switch
        {
            ".mid" or ".midi" => "MIDI",
            ".mml" => "MML",
            ".txt" => "MML(TXT)",
            _ => "-"
        };
    }

    public void SetImporting()
    {
        Status = ImportItemStatus.Importing;
        StatusText = "가져오는 중";
        IsImporting = true;
        IsCompleted = false;
        IsFailed = false;
        ErrorMessage = string.Empty;
    }

    public void SetCompleted(ImportResult result)
    {
        Result = result;
        CreatedScore = result.CreatedScoreItem;
        Status = ImportItemStatus.Completed;
        StatusText = "완료";
        IsImporting = false;
        IsCompleted = true;
        IsFailed = false;

        SourceType = result.SourceType.ToString().ToUpperInvariant();
        var ts = TimeSpan.FromSeconds(result.Duration);
        DurationText = $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
        NotesText = $"{result.NoteCount:N0}개";
        BpmText = $"{Math.Round(result.InitialBpm)}";
    }

    public void SetFailed(string error)
    {
        Status = ImportItemStatus.Failed;
        StatusText = "실패";
        IsImporting = false;
        IsCompleted = false;
        IsFailed = true;
        ErrorMessage = error;
    }

    public void SetCancelled()
    {
        Status = ImportItemStatus.Cancelled;
        StatusText = "취소됨";
        IsImporting = false;
        IsCompleted = false;
        IsFailed = false;
        ErrorMessage = "사용자에 의해 취소됨";
    }
}
