using RobloxPiano.Core.Library;
using RobloxPiano.Core.Music;

namespace RobloxPiano.Core.Importing;

public class ImportResult
{
    public bool Success { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public ImportSourceType SourceType { get; set; } = ImportSourceType.Unknown;
    public string Title { get; set; } = string.Empty;
    public MusicTimeline? Timeline { get; set; }
    public double Duration { get; set; }
    public double InitialBpm { get; set; }
    public int NoteCount { get; set; }
    public int TrackCount { get; set; }
    public int PlayableNoteCount { get; set; }
    public int OutOfRangeNoteCount { get; set; }
    public int? MinPitch { get; set; }
    public int? MaxPitch { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public ScoreItem? CreatedScoreItem { get; set; }

    public static ImportResult Failed(
        string filePath,
        string errorMessage,
        string? errorCode = null,
        ImportSourceType sourceType = ImportSourceType.Unknown)
    {
        return new ImportResult
        {
            Success = false,
            FilePath = filePath,
            SourceType = sourceType,
            Title = Path.GetFileNameWithoutExtension(filePath),
            ErrorMessage = errorMessage,
            ErrorCode = errorCode
        };
    }

    public static ImportResult Successful(
        string filePath,
        ImportSourceType sourceType,
        string title,
        MusicTimeline timeline,
        int playableNotes,
        int outOfRangeNotes,
        int minPitch,
        int maxPitch,
        ScoreItem? createdScore = null)
    {
        int trackCount = timeline.TrackNames.Count > 0 ? timeline.TrackNames.Count : 1;
        return new ImportResult
        {
            Success = true,
            FilePath = filePath,
            SourceType = sourceType,
            Title = title,
            Timeline = timeline,
            Duration = timeline.Duration,
            InitialBpm = timeline.InitialBpm,
            NoteCount = timeline.TotalNotes,
            TrackCount = trackCount,
            PlayableNoteCount = playableNotes,
            OutOfRangeNoteCount = outOfRangeNotes,
            MinPitch = minPitch,
            MaxPitch = maxPitch,
            CreatedScoreItem = createdScore
        };
    }
}
