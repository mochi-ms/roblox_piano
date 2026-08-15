namespace RobloxPiano.Core.Music;

public class PedalEvent
{
    public double Time { get; set; }
    public bool Down { get; set; }
    public int? Value { get; set; }
    public string Source { get; set; } = string.Empty;

    public PedalEvent() { }

    public PedalEvent(double time, bool down, int? value = null, string source = "")
    {
        Time = time;
        Down = down;
        Value = value;
        Source = source;
    }
}
