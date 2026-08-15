namespace RobloxPiano.Core.Music;

public static class RangeProcessor
{
    public const int DefaultMinPitch = 21;  // A0
    public const int DefaultMaxPitch = 108; // C8
    public const int Roblox61MinPitch = 36; // C2
    public const int Roblox61MaxPitch = 96; // C7

    public static RangeAnalysisResult AnalyzeRange(
        MusicTimeline timeline,
        int minPitch = DefaultMinPitch,
        int maxPitch = DefaultMaxPitch)
    {
        if (timeline.Notes.Count == 0)
        {
            return new RangeAnalysisResult(0, 0, 0, 60, 60, new List<NoteEvent>(), 0);
        }

        var outNotes = new List<NoteEvent>();
        var pitches = new List<int>();

        foreach (var n in timeline.Notes)
        {
            pitches.Add(n.Pitch);
            if (n.Pitch < minPitch || n.Pitch > maxPitch)
            {
                outNotes.Add(n);
            }
        }

        int minP = pitches.Min();
        int maxP = pitches.Max();

        int span = maxP - minP;
        int suggested = 0;
        if (span <= (maxPitch - minPitch))
        {
            if (minP < minPitch)
            {
                suggested = minPitch - minP;
            }
            else if (maxP > maxPitch)
            {
                suggested = maxPitch - maxP;
            }
        }

        return new RangeAnalysisResult(
            totalNotes: timeline.Notes.Count,
            inRangeCount: timeline.Notes.Count - outNotes.Count,
            outOfRangeCount: outNotes.Count,
            minPitch: minP,
            maxPitch: maxP,
            outOfRangeNotes: outNotes,
            suggestedTranspose: suggested
        );
    }

    public static int ApplyOctaveFit(
        MusicTimeline timeline,
        int minPitch = DefaultMinPitch,
        int maxPitch = DefaultMaxPitch)
    {
        int modifiedCount = 0;
        foreach (var n in timeline.Notes)
        {
            if (!n.OriginalPitch.HasValue)
            {
                n.OriginalPitch = n.Pitch;
            }

            int currPitch = n.Pitch;
            bool adjusted = false;

            while (currPitch < minPitch)
            {
                currPitch += 12;
                adjusted = true;
            }

            while (currPitch > maxPitch)
            {
                currPitch -= 12;
                adjusted = true;
            }

            if (currPitch < minPitch)
            {
                currPitch = minPitch;
                adjusted = true;
            }
            else if (currPitch > maxPitch)
            {
                currPitch = maxPitch;
                adjusted = true;
            }

            if (adjusted)
            {
                n.Pitch = currPitch;
                modifiedCount++;
            }
        }

        return modifiedCount;
    }
}
