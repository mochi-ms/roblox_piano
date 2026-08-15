using RobloxPiano.Core.Music;

namespace RobloxPiano.Core.Importing;

public static class ImportTimelineValidator
{
    public static ImportValidationResult Validate(MusicTimeline? timeline)
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

        // Calculate diagnostics
        int playableNotes = timeline.Notes.Count(n => n.Pitch >= 36 && n.Pitch <= 96);
        int outOfRangeNotes = timeline.Notes.Count(n => n.Pitch < 36 || n.Pitch > 96);
        var (minPitch, maxPitch) = timeline.PitchRange;

        return ImportValidationResult.Valid(
            totalNotes: timeline.TotalNotes,
            playableNotes: playableNotes,
            outOfRangeNotes: outOfRangeNotes,
            minPitch: minPitch,
            maxPitch: maxPitch);
    }
}
