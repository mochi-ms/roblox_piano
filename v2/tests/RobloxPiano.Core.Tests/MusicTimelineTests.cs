using RobloxPiano.Core.Music;
using Xunit;

namespace RobloxPiano.Core.Tests;

public class MusicTimelineTests
{
    [Fact]
    public void Timeline_EmptyProperties_ReturnsSensibleDefaults()
    {
        var timeline = new MusicTimeline("Empty");
        Assert.Equal(0, timeline.TotalNotes);
        Assert.Equal(0.0, timeline.Duration);
        Assert.Equal((60, 60), timeline.PitchRange);
        Assert.Equal((0, 0, 0), timeline.GetHandNoteCounts());
    }

    [Fact]
    public void Timeline_SortEvents_SortsByTimeAndPitch()
    {
        var timeline = new MusicTimeline("SortTest");
        timeline.AddNote(new NoteEvent(64, 1.0, 2.0));
        timeline.AddNote(new NoteEvent(60, 1.0, 2.0));
        timeline.AddNote(new NoteEvent(55, 0.5, 1.5));
        timeline.AddPedal(new PedalEvent(2.0, false));
        timeline.AddPedal(new PedalEvent(0.5, true));

        timeline.SortEvents();

        Assert.Equal(55, timeline.Notes[0].Pitch);
        Assert.Equal(60, timeline.Notes[1].Pitch);
        Assert.Equal(64, timeline.Notes[2].Pitch);

        Assert.True(timeline.Pedals[0].Down);
        Assert.False(timeline.Pedals[1].Down);
    }

    [Fact]
    public void Timeline_Duration_AccountsForNotesAndPedals()
    {
        var timeline = new MusicTimeline("DurationTest");
        timeline.AddNote(new NoteEvent(60, 0.0, 3.5));
        timeline.AddPedal(new PedalEvent(4.2, false));

        Assert.Equal(4.2, timeline.Duration, precision: 5);
    }

    [Fact]
    public void Timeline_PitchRange_ReturnsMinAndMax()
    {
        var timeline = new MusicTimeline("RangeTest");
        timeline.AddNote(new NoteEvent(48, 0, 1));
        timeline.AddNote(new NoteEvent(72, 0, 1));
        timeline.AddNote(new NoteEvent(60, 0, 1));

        Assert.Equal((48, 72), timeline.PitchRange);
    }

    [Fact]
    public void Timeline_GetHandNoteCounts_CalculatesCorrectly()
    {
        var timeline = new MusicTimeline("HandTest");
        timeline.AddNote(new NoteEvent(60, 0, 1, hand: HandType.Right));
        timeline.AddNote(new NoteEvent(62, 0, 1, hand: HandType.Right));
        timeline.AddNote(new NoteEvent(48, 0, 1, hand: HandType.Left));
        timeline.AddNote(new NoteEvent(50, 0, 1, hand: HandType.Auto));

        var (rh, lh, other) = timeline.GetHandNoteCounts();
        Assert.Equal(2, rh);
        Assert.Equal(1, lh);
        Assert.Equal(1, other);
    }

    [Fact]
    public void Timeline_GetFilteredNotes_FiltersHandsAndTracks()
    {
        var timeline = new MusicTimeline("FilterTest");
        timeline.AddNote(new NoteEvent(60, 0, 1, hand: HandType.Right, track: 0));
        timeline.AddNote(new NoteEvent(48, 0, 1, hand: HandType.Left, track: 1));
        timeline.AddNote(new NoteEvent(55, 0, 1, hand: HandType.Both, track: 0));

        // Disable LH
        var rhOnly = timeline.GetFilteredNotes(enableRh: true, enableLh: false);
        Assert.Equal(2, rhOnly.Count);
        Assert.Contains(rhOnly, n => n.Pitch == 60);
        Assert.Contains(rhOnly, n => n.Pitch == 55);

        // Track filter (disable track 1)
        var trackFilter = new Dictionary<int, bool> { [0] = true, [1] = false };
        var trackFiltered = timeline.GetFilteredNotes(trackFilter: trackFilter);
        Assert.Equal(2, trackFiltered.Count);
        Assert.DoesNotContain(trackFiltered, n => n.Track == 1);
    }

    [Fact]
    public void Timeline_BuildChordGroups_GroupsSimultaneousNotesWithinTolerance()
    {
        var timeline = new MusicTimeline("ChordTest");
        // Chord 1 at t = 0.000, 0.005, 0.010 (within 15ms tolerance from group start 0.000)
        timeline.AddNote(new NoteEvent(60, 0.000, 1.0));
        timeline.AddNote(new NoteEvent(64, 0.005, 1.0));
        timeline.AddNote(new NoteEvent(67, 0.010, 1.0));

        // Chord 2 at t = 0.050 (separated by 50ms)
        timeline.AddNote(new NoteEvent(72, 0.050, 1.5));

        var chordGroups = timeline.BuildChordGroups(tolerance: 0.015);
        Assert.Equal(2, chordGroups.Count);
        Assert.Equal(3, chordGroups[0].Notes.Count);
        Assert.Single(chordGroups[1].Notes);
        Assert.Equal(1.0, chordGroups[0].MaxEndTime, precision: 5);
        Assert.Equal(1.5, chordGroups[1].MaxEndTime, precision: 5);
    }
}
