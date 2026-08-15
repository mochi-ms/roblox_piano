namespace RobloxPiano.Core.Importers;

public class MmlParseException : Exception
{
    public int TrackIndex { get; }
    public int Position { get; }
    public string Token { get; }
    public string CustomMessage { get; }

    public MmlParseException(int trackIndex, int position, string token, string customMessage = "")
        : base(FormatMessage(trackIndex, position, token, customMessage))
    {
        TrackIndex = trackIndex;
        Position = position;
        Token = token;
        CustomMessage = customMessage;
    }

    private static string FormatMessage(int trackIndex, int position, string token, string customMessage)
    {
        var msg = $"Track {trackIndex + 1}, Position {position}: Unexpected token '{token}'";
        if (!string.IsNullOrEmpty(customMessage))
        {
            msg += $" ({customMessage})";
        }
        return msg;
    }
}
