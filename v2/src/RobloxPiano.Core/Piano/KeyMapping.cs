namespace RobloxPiano.Core.Piano;

public class KeyMapping
{
    public int Pitch { get; set; }
    public string Char { get; set; } = string.Empty;
    public string PhysicalKey { get; set; } = string.Empty;
    public HashSet<string> Modifiers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Name { get; set; } = string.Empty;

    public KeyMapping() { }

    public KeyMapping(
        int pitch,
        string @char,
        string physicalKey,
        IEnumerable<string>? modifiers = null,
        string name = "")
    {
        Pitch = pitch;
        Char = @char;
        PhysicalKey = physicalKey;
        if (modifiers != null)
        {
            foreach (var m in modifiers)
            {
                Modifiers.Add(m);
            }
        }
        Name = name;
    }
}
