namespace RobloxPiano.Core.Audio;

public enum AudioSourceType
{
    Unknown = 0,
    Mp3 = 1,
    Wav = 2,
    M4a = 3,
    Flac = 4,
    Aac = 5,
    Ogg = 6
}

public static class AudioSourceTypeExtensions
{
    public static AudioSourceType FromExtension(string filePathOrExtension)
    {
        if (string.IsNullOrWhiteSpace(filePathOrExtension)) return AudioSourceType.Unknown;

        string ext = Path.GetExtension(filePathOrExtension).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext))
        {
            ext = filePathOrExtension.StartsWith('.') ? filePathOrExtension.ToLowerInvariant() : "." + filePathOrExtension.ToLowerInvariant();
        }

        return ext switch
        {
            ".mp3" => AudioSourceType.Mp3,
            ".wav" => AudioSourceType.Wav,
            ".m4a" => AudioSourceType.M4a,
            ".flac" => AudioSourceType.Flac,
            ".aac" => AudioSourceType.Aac,
            ".ogg" => AudioSourceType.Ogg,
            _ => AudioSourceType.Unknown
        };
    }

    public static string ToFriendlyString(this AudioSourceType type) => type switch
    {
        AudioSourceType.Mp3 => "MP3",
        AudioSourceType.Wav => "WAV",
        AudioSourceType.M4a => "M4A",
        AudioSourceType.Flac => "FLAC",
        AudioSourceType.Aac => "AAC",
        AudioSourceType.Ogg => "OGG",
        _ => "알 수 없음"
    };
}
