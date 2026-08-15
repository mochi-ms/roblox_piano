using RobloxPiano.Core.Music;

namespace RobloxPiano.Core.Piano;

public class RobloxPianoMapper
{
    public PianoProfile Profile { get; private set; }
    private readonly Dictionary<string, KeyMapping> _charToKeyMap = new(StringComparer.Ordinal);

    public RobloxPianoMapper(PianoProfile? profile = null)
    {
        Profile = profile ?? PianoProfileLoader.LoadDefaultProfile();
        RebuildCache();
    }

    public void SetProfile(PianoProfile profile)
    {
        Profile = profile;
        RebuildCache();
    }

    private void RebuildCache()
    {
        _charToKeyMap.Clear();
        foreach (var km in Profile.Keys.Values)
        {
            if (!string.IsNullOrEmpty(km.Char))
            {
                _charToKeyMap[km.Char] = km;
            }
        }
    }

    public KeyMapping? MapPitch(int pitch)
    {
        return Profile.Keys.TryGetValue(pitch, out var km) ? km : null;
    }

    public KeyMapping? MapNoteEvent(NoteEvent note)
    {
        return MapPitch(note.Pitch);
    }

    public bool CanPlay(int pitch)
    {
        return Profile.Keys.ContainsKey(pitch);
    }

    public KeyMapping? GetByChar(string charKey)
    {
        return _charToKeyMap.TryGetValue(charKey, out var km) ? km : null;
    }

    public int MinPitch => Profile.MinPitch;
    public int MaxPitch => Profile.MaxPitch;
}
