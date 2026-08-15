namespace RobloxPiano.Core.Music;

public class RangeAnalysisResult
{
    public int TotalNotes { get; set; }
    public int InRangeCount { get; set; }
    public int OutOfRangeCount { get; set; }
    public int MinPitch { get; set; }
    public int MaxPitch { get; set; }
    public List<NoteEvent> OutOfRangeNotes { get; set; } = new();
    public int SuggestedTranspose { get; set; }

    public RangeAnalysisResult() { }

    public RangeAnalysisResult(
        int totalNotes,
        int inRangeCount,
        int outOfRangeCount,
        int minPitch,
        int maxPitch,
        List<NoteEvent> outOfRangeNotes,
        int suggestedTranspose = 0)
    {
        TotalNotes = totalNotes;
        InRangeCount = inRangeCount;
        OutOfRangeCount = outOfRangeCount;
        MinPitch = minPitch;
        MaxPitch = maxPitch;
        OutOfRangeNotes = outOfRangeNotes;
        SuggestedTranspose = suggestedTranspose;
    }
}
