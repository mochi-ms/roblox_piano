using RobloxPiano.Core.Music;
using Xunit;

namespace RobloxPiano.Core.Tests;

public class RangeProcessorTests
{
    [Fact]
    public void AnalyzeRange_EmptyTimeline_ReturnsZeroResult()
    {
        var timeline = new MusicTimeline("Empty");
        var result = RangeProcessor.AnalyzeRange(timeline);

        Assert.Equal(0, result.TotalNotes);
        Assert.Equal(0, result.InRangeCount);
        Assert.Equal(0, result.OutOfRangeCount);
        Assert.Equal(0, result.SuggestedTranspose);
    }

    [Fact]
    public void AnalyzeRange_NotesWithinRange_IdentifiesCorrectCounts()
    {
        var timeline = new MusicTimeline("AnalyzeTest");
        timeline.AddNote(new NoteEvent(40, 0, 1));
        timeline.AddNote(new NoteEvent(60, 0, 1));
        timeline.AddNote(new NoteEvent(90, 0, 1));
        timeline.AddNote(new NoteEvent(100, 0, 1)); // Out of Roblox 61-key range (36-96)

        var result = RangeProcessor.AnalyzeRange(timeline, minPitch: 36, maxPitch: 96);
        Assert.Equal(4, result.TotalNotes);
        Assert.Equal(3, result.InRangeCount);
        Assert.Equal(1, result.OutOfRangeCount);
        Assert.Equal(40, result.MinPitch);
        Assert.Equal(100, result.MaxPitch);
    }

    [Fact]
    public void AnalyzeRange_SuggestsTranspose_WhenSpanFits()
    {
        var timeline = new MusicTimeline("TransposeTest");
        // Span is 50-20 = 30 semitones, which fits in 60 semitones (36..96)
        timeline.AddNote(new NoteEvent(20, 0, 1));
        timeline.AddNote(new NoteEvent(50, 0, 1));

        var result = RangeProcessor.AnalyzeRange(timeline, minPitch: 36, maxPitch: 96);
        Assert.Equal(36 - 20, result.SuggestedTranspose); // +16
    }

    [Fact]
    public void ApplyOctaveFit_AdjustsLowAndHighNotes_PreservesOriginalPitch()
    {
        var timeline = new MusicTimeline("OctaveFitTest");
        var lowNote = new NoteEvent(20, 0, 1);   // Needs +12 -> 32, +12 -> 44
        var inRangeNote = new NoteEvent(60, 0, 1);
        var highNote = new NoteEvent(110, 0, 1); // Needs -12 -> 98, -12 -> 86

        timeline.AddNote(lowNote);
        timeline.AddNote(inRangeNote);
        timeline.AddNote(highNote);

        int modified = RangeProcessor.ApplyOctaveFit(timeline, minPitch: 36, maxPitch: 96);

        Assert.Equal(2, modified);
        Assert.Equal(44, lowNote.Pitch);
        Assert.Equal(20, lowNote.OriginalPitch);

        Assert.Equal(60, inRangeNote.Pitch);
        Assert.Equal(60, inRangeNote.OriginalPitch);

        Assert.Equal(86, highNote.Pitch);
        Assert.Equal(110, highNote.OriginalPitch);
    }
}
