using RobloxPiano.Core.Music;
using RobloxPiano.Core.Piano;

namespace RobloxPiano.Core.Importing;

public static class ImportTimelineValidator
{
    public static ImportValidationResult Validate(MusicTimeline? timeline, PianoProfile? targetProfile = null)
    {
        if (timeline == null)
        {
            return ImportValidationResult.Invalid("타임라인을 생성할 수 없습니다.");
        }

        if (timeline.Notes.Count == 0)
        {
            return ImportValidationResult.Invalid(ImportError.NoPlayableNotes);
        }

        // Validate InitialBpm (strictly reject NaN, Infinity, and non-positive BPM without silent fallback)
        if (double.IsNaN(timeline.InitialBpm) || double.IsInfinity(timeline.InitialBpm) || timeline.InitialBpm <= 0)
        {
            return ImportValidationResult.Invalid(ImportError.CorruptTiming);
        }

        // Validate Duration
        if (double.IsNaN(timeline.Duration) || double.IsInfinity(timeline.Duration) || timeline.Duration < 0)
        {
            return ImportValidationResult.Invalid(ImportError.CorruptTiming);
        }

        // Validate each NoteEvent
        foreach (var note in timeline.Notes)
        {
            if (double.IsNaN(note.StartTime) || double.IsInfinity(note.StartTime) || note.StartTime < 0 ||
                double.IsNaN(note.EndTime) || double.IsInfinity(note.EndTime) || note.EndTime <= note.StartTime ||
                note.Pitch < 0 || note.Pitch > 127)
            {
                return ImportValidationResult.Invalid(ImportError.CorruptTiming);
            }
        }

        // Calculate diagnostics using actual PianoProfile (defaults to 88-key)
        var profile = targetProfile ?? PianoProfileLoader.LoadDefaultProfile();
        int playableNotes = timeline.Notes.Count(n => profile.Keys.ContainsKey(n.Pitch));
        int outOfRangeNotes = timeline.Notes.Count(n => !profile.Keys.ContainsKey(n.Pitch));
        var (minPitch, maxPitch) = timeline.PitchRange;

        return ImportValidationResult.Valid(
            totalNotes: timeline.TotalNotes,
            playableNotes: playableNotes,
            outOfRangeNotes: outOfRangeNotes,
            minPitch: minPitch,
            maxPitch: maxPitch);
    }
}
