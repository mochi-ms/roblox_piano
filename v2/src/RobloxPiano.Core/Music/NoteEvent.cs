namespace RobloxPiano.Core.Music;

public class NoteEvent
{
    public int Pitch { get; set; }
    public double StartTime { get; set; }
    public double EndTime { get; set; }
    public int Velocity { get; set; } = 64;
    public HandType Hand { get; set; } = HandType.Auto;
    public int? Staff { get; set; }
    public int? Track { get; set; }
    public int? Channel { get; set; } = 0;
    public string Source { get; set; } = "default";
    public int? OriginalPitch { get; set; }

    public NoteEvent() { }

    public NoteEvent(
        int pitch,
        double startTime,
        double endTime,
        int velocity = 64,
        HandType hand = HandType.Auto,
        int? staff = null,
        int? track = null,
        int? channel = 0,
        string source = "default",
        int? originalPitch = null)
    {
        Pitch = pitch;
        StartTime = startTime;
        EndTime = endTime;
        Velocity = velocity;
        Hand = hand;
        Staff = staff;
        Track = track;
        Channel = channel;
        Source = source;
        OriginalPitch = originalPitch;
    }

    public double Duration => Math.Max(0.01, EndTime - StartTime);

    public bool IsInRange(int minPitch = 36, int maxPitch = 96)
    {
        return minPitch <= Pitch && Pitch <= maxPitch;
    }
}
