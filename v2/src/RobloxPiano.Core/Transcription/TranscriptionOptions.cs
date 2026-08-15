namespace RobloxPiano.Core.Transcription;

public record TranscriptionOptions(
    double OnsetThreshold = 0.5,
    double FrameThreshold = 0.3,
    double MinimumNoteLengthMs = 127.7
)
{
    public static TranscriptionOptions Default => new();

    public void Validate()
    {
        if (double.IsNaN(OnsetThreshold) || double.IsInfinity(OnsetThreshold) || OnsetThreshold <= 0.0 || OnsetThreshold >= 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(OnsetThreshold), "OnsetThreshold는 0.0과 1.0 사이의 값이어야 합니다.");
        }

        if (double.IsNaN(FrameThreshold) || double.IsInfinity(FrameThreshold) || FrameThreshold <= 0.0 || FrameThreshold >= 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(FrameThreshold), "FrameThreshold는 0.0과 1.0 사이의 값이어야 합니다.");
        }

        if (double.IsNaN(MinimumNoteLengthMs) || double.IsInfinity(MinimumNoteLengthMs) || MinimumNoteLengthMs <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumNoteLengthMs), "MinimumNoteLengthMs는 0보다 큰 유한한 값이어야 합니다.");
        }
    }
}
