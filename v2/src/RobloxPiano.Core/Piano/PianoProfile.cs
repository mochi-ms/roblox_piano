namespace RobloxPiano.Core.Piano;

public class PianoProfile
{
    public string Name { get; set; } = "Unknown Profile";
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";
    public int MinPitch { get; set; } = 36;
    public int MaxPitch { get; set; } = 96;
    public Dictionary<int, KeyMapping> Keys { get; set; } = new();
    public string? SustainPedal { get; set; }
    public string? FilePath { get; set; }

    public PianoProfile() { }

    public PianoProfile(
        string name,
        string description,
        string version,
        int minPitch,
        int maxPitch,
        Dictionary<int, KeyMapping>? keys = null,
        string? sustainPedal = null,
        string? filePath = null)
    {
        Name = name;
        Description = description;
        Version = version;
        MinPitch = minPitch;
        MaxPitch = maxPitch;
        Keys = keys ?? new Dictionary<int, KeyMapping>();
        SustainPedal = sustainPedal;
        FilePath = filePath;
    }
}
