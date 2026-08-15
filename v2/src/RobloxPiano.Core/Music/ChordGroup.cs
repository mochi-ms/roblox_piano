namespace RobloxPiano.Core.Music;

public class ChordGroup
{
    public double StartTime { get; set; }
    public List<NoteEvent> Notes { get; set; } = new();

    public ChordGroup() { }

    public ChordGroup(double startTime, List<NoteEvent>? notes = null)
    {
        StartTime = startTime;
        if (notes != null)
        {
            Notes = notes;
        }
    }

    public double MaxEndTime
    {
        get
        {
            if (Notes.Count == 0)
                return StartTime;
            return Notes.Max(n => n.EndTime);
        }
    }
}
