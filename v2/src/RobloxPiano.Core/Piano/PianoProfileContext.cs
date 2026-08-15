namespace RobloxPiano.Core.Piano;

public enum RobloxPianoProfileKind
{
    Key88,
    Key61
}

public sealed class PianoProfileContext
{
    public PianoProfile CurrentProfile { get; private set; }
    public RobloxPianoProfileKind CurrentKind { get; private set; }
    public event EventHandler<PianoProfile>? ProfileChanged;

    public PianoProfileContext(PianoProfile? initialProfile = null)
    {
        CurrentProfile = initialProfile ?? PianoProfileLoader.LoadDefaultProfile();
        CurrentKind = (CurrentProfile.MinPitch <= 21 && CurrentProfile.MaxPitch >= 108)
            ? RobloxPianoProfileKind.Key88
            : RobloxPianoProfileKind.Key61;
    }

    public void SetProfile(PianoProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        CurrentProfile = profile;
        CurrentKind = (profile.MinPitch <= 21 && profile.MaxPitch >= 108)
            ? RobloxPianoProfileKind.Key88
            : RobloxPianoProfileKind.Key61;

        ProfileChanged?.Invoke(this, profile);
    }

    public void SetKind(RobloxPianoProfileKind kind)
    {
        var profile = kind == RobloxPianoProfileKind.Key61
            ? PianoProfileLoader.Load61KeyProfile()
            : PianoProfileLoader.Load88KeyProfile();
        SetProfile(profile);
    }
}
