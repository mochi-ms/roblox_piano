namespace RobloxPiano.Core.Importing;

public class ImportValidationResult
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public int TotalNotes { get; set; }
    public int PlayableNotes { get; set; }
    public int OutOfRangeNotes { get; set; }
    public int MinPitch { get; set; } = 60;
    public int MaxPitch { get; set; } = 60;

    public static ImportValidationResult Valid(int totalNotes, int playableNotes, int outOfRangeNotes, int minPitch, int maxPitch)
    {
        return new ImportValidationResult
        {
            IsValid = true,
            TotalNotes = totalNotes,
            PlayableNotes = playableNotes,
            OutOfRangeNotes = outOfRangeNotes,
            MinPitch = minPitch,
            MaxPitch = maxPitch
        };
    }

    public static ImportValidationResult Invalid(string errorMessage)
    {
        return new ImportValidationResult
        {
            IsValid = false,
            ErrorMessage = errorMessage
        };
    }
}
