namespace RobloxPiano.Core.Music;

public static class HandAssignmentService
{
    private static readonly string[] RightKeywords = ["right", "rh", "treble", "upper", "soprano", "melody"];
    private static readonly string[] LeftKeywords = ["left", "lh", "bass", "lower", "accomp"];

    public static void AssignHandsToTimeline(
        MusicTimeline timeline,
        Dictionary<int, HandType>? trackHandOverrides = null,
        int splitPoint = 60)
    {
        var overrides = trackHandOverrides ?? new Dictionary<int, HandType>();

        // 1. Inferred from track names
        var trackInferred = new Dictionary<int, HandType>();
        foreach (var (trackIdx, name) in timeline.TrackNames)
        {
            if (overrides.TryGetValue(trackIdx, out var overrideHand))
            {
                trackInferred[trackIdx] = overrideHand;
                continue;
            }

            var nameLower = name.ToLowerInvariant();
            if (RightKeywords.Any(k => nameLower.Contains(k)))
            {
                trackInferred[trackIdx] = HandType.Right;
            }
            else if (LeftKeywords.Any(k => nameLower.Contains(k)))
            {
                trackInferred[trackIdx] = HandType.Left;
            }
            else
            {
                trackInferred[trackIdx] = HandType.Auto;
            }
        }

        // 2. Assign to each note
        foreach (var note in timeline.Notes)
        {
            // 1. User override
            if (note.Track.HasValue && overrides.TryGetValue(note.Track.Value, out var overrideHand))
            {
                note.Hand = overrideHand;
                continue;
            }

            // 2. Track inferred
            if (note.Track.HasValue && trackInferred.TryGetValue(note.Track.Value, out var inferredHand) && inferredHand != HandType.Auto)
            {
                note.Hand = inferredHand;
                continue;
            }

            // 3. Staff (MusicXML)
            if (note.Staff == 1)
            {
                note.Hand = HandType.Right;
                continue;
            }
            if (note.Staff == 2)
            {
                note.Hand = HandType.Left;
                continue;
            }

            // 4. Fallback pitch split
            note.Hand = (note.Pitch >= splitPoint) ? HandType.Right : HandType.Left;
        }
    }
}
