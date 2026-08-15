namespace RobloxPiano.Core.Music;

public class MusicTimeline
{
    public string Title { get; set; } = "Untitled";
    public List<NoteEvent> Notes { get; set; } = new();
    public List<PedalEvent> Pedals { get; set; } = new();
    public double InitialBpm { get; set; } = 120.0;
    public (int Numerator, int Denominator) TimeSignature { get; set; } = (4, 4);
    public Dictionary<int, string> TrackNames { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();

    public MusicTimeline(string title = "Untitled")
    {
        Title = title;
    }

    public void AddNote(NoteEvent note)
    {
        Notes.Add(note);
    }

    public void AddPedal(PedalEvent pedal)
    {
        Pedals.Add(pedal);
    }

    public void SortEvents()
    {
        Notes.Sort((a, b) =>
        {
            int cmp = a.StartTime.CompareTo(b.StartTime);
            if (cmp != 0) return cmp;
            return a.Pitch.CompareTo(b.Pitch);
        });

        Pedals.Sort((a, b) => a.Time.CompareTo(b.Time));
    }

    public int TotalNotes => Notes.Count;

    public double Duration
    {
        get
        {
            double noteDur = Notes.Count > 0 ? Notes.Max(n => n.EndTime) : 0.0;
            double pedalDur = Pedals.Count > 0 ? Pedals.Max(p => p.Time) : 0.0;
            return Math.Max(noteDur, pedalDur);
        }
    }

    public (int MinPitch, int MaxPitch) PitchRange
    {
        get
        {
            if (Notes.Count == 0)
                return (60, 60);
            return (Notes.Min(n => n.Pitch), Notes.Max(n => n.Pitch));
        }
    }

    public (int RightHandCount, int LeftHandCount, int OtherCount) GetHandNoteCounts()
    {
        int rh = Notes.Count(n => n.Hand == HandType.Right);
        int lh = Notes.Count(n => n.Hand == HandType.Left);
        int other = Notes.Count - rh - lh;
        return (rh, lh, other);
    }

    public List<NoteEvent> GetOutOfRangeNotes(int minPitch = 36, int maxPitch = 96)
    {
        return Notes.Where(n => n.Pitch < minPitch || n.Pitch > maxPitch).ToList();
    }

    public List<NoteEvent> GetFilteredNotes(
        bool enableRh = true,
        bool enableLh = true,
        Dictionary<int, bool>? trackFilter = null)
    {
        var filtered = new List<NoteEvent>();
        foreach (var n in Notes)
        {
            if (trackFilter != null && n.Track.HasValue)
            {
                if (trackFilter.TryGetValue(n.Track.Value, out bool isEnabled) && !isEnabled)
                    continue;
            }

            if (n.Hand == HandType.Right && !enableRh)
                continue;
            if (n.Hand == HandType.Left && !enableLh)
                continue;
            if (n.Hand != HandType.Right && n.Hand != HandType.Left)
            {
                if (!enableRh && !enableLh)
                    continue;
            }

            filtered.Add(n);
        }
        return filtered;
    }

    public List<ChordGroup> BuildChordGroups(List<NoteEvent>? notes = null, double tolerance = 0.015)
    {
        var targetNotes = notes ?? Notes;
        if (targetNotes.Count == 0)
            return new List<ChordGroup>();

        var sortedNotes = targetNotes
            .OrderBy(n => n.StartTime)
            .ThenBy(n => n.Pitch)
            .ToList();

        var chordGroups = new List<ChordGroup>();
        ChordGroup? currentGroup = null;

        foreach (var note in sortedNotes)
        {
            if (currentGroup == null)
            {
                currentGroup = new ChordGroup(note.StartTime, new List<NoteEvent> { note });
            }
            else if (Math.Abs(note.StartTime - currentGroup.StartTime) <= tolerance)
            {
                currentGroup.Notes.Add(note);
            }
            else
            {
                chordGroups.Add(currentGroup);
                currentGroup = new ChordGroup(note.StartTime, new List<NoteEvent> { note });
            }
        }

        if (currentGroup != null)
        {
            chordGroups.Add(currentGroup);
        }

        return chordGroups;
    }
}
