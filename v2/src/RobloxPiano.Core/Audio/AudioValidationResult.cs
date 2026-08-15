namespace RobloxPiano.Core.Audio;

public class AudioValidationResult
{
    public bool IsValid { get; }
    public string? ErrorMessage { get; }
    public AudioMetadata? Metadata { get; }

    private AudioValidationResult(bool isValid, string? errorMessage, AudioMetadata? metadata)
    {
        IsValid = isValid;
        ErrorMessage = errorMessage;
        Metadata = metadata;
    }

    public static AudioValidationResult Valid(AudioMetadata metadata) =>
        new(true, null, metadata);

    public static AudioValidationResult Invalid(string errorMessage, AudioMetadata? metadata = null) =>
        new(false, errorMessage, metadata);
}
