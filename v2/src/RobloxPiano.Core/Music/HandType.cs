namespace RobloxPiano.Core.Music;

public enum HandType
{
    Right,
    Left,
    Both,
    Auto,
    Unknown
}

public static class HandTypeExtensions
{
    public static HandType FromString(string? val)
    {
        if (string.IsNullOrWhiteSpace(val))
            return HandType.Unknown;

        var valUpper = val.Trim().ToUpperInvariant();
        return valUpper switch
        {
            "RH" or "RIGHT" or "UPPER" or "TREBLE" or "SOPRANO" or "MELODY" => HandType.Right,
            "LH" or "LEFT" or "LOWER" or "BASS" or "ACCOMP" => HandType.Left,
            "BOTH" or "ALL" => HandType.Both,
            "AUTO" => HandType.Auto,
            _ => HandType.Unknown
        };
    }
}
