using RobloxPiano.Core.Music;
using Xunit;

namespace RobloxPiano.Core.Tests;

public class HandAssignmentServiceTests
{
    [Fact]
    public void AssignHands_UserOverride_TakesTopPriority()
    {
        var timeline = new MusicTimeline("OverrideTest");
        timeline.TrackNames[0] = "Treble Melody"; // would infer Right
        var note = new NoteEvent(72, 0, 1, track: 0);
        timeline.AddNote(note);

        var overrides = new Dictionary<int, HandType> { [0] = HandType.Left };
        HandAssignmentService.AssignHandsToTimeline(timeline, trackHandOverrides: overrides);

        Assert.Equal(HandType.Left, note.Hand);
    }

    [Fact]
    public void AssignHands_TrackNames_InfersRightAndLeft()
    {
        var timeline = new MusicTimeline("TrackNameTest");
        timeline.TrackNames[0] = "Piano Right Hand";
        timeline.TrackNames[1] = "Acoustic Bass (LH)";

        var noteRh = new NoteEvent(48, 0, 1, track: 0); // Low pitch but on RH track
        var noteLh = new NoteEvent(72, 0, 1, track: 1); // High pitch but on LH track

        timeline.AddNote(noteRh);
        timeline.AddNote(noteLh);

        HandAssignmentService.AssignHandsToTimeline(timeline);

        Assert.Equal(HandType.Right, noteRh.Hand);
        Assert.Equal(HandType.Left, noteLh.Hand);
    }

    [Fact]
    public void AssignHands_StaffMapping_AssignsStaff1RightAndStaff2Left()
    {
        var timeline = new MusicTimeline("StaffTest");
        var noteStaff1 = new NoteEvent(40, 0, 1, staff: 1);
        var noteStaff2 = new NoteEvent(80, 0, 1, staff: 2);

        timeline.AddNote(noteStaff1);
        timeline.AddNote(noteStaff2);

        HandAssignmentService.AssignHandsToTimeline(timeline);

        Assert.Equal(HandType.Right, noteStaff1.Hand);
        Assert.Equal(HandType.Left, noteStaff2.Hand);
    }

    [Fact]
    public void AssignHands_PitchSplitFallback_SplitsAtC4()
    {
        var timeline = new MusicTimeline("FallbackTest");
        var noteC4 = new NoteEvent(60, 0, 1);
        var noteB3 = new NoteEvent(59, 0, 1);

        timeline.AddNote(noteC4);
        timeline.AddNote(noteB3);

        HandAssignmentService.AssignHandsToTimeline(timeline, splitPoint: 60);

        Assert.Equal(HandType.Right, noteC4.Hand);
        Assert.Equal(HandType.Left, noteB3.Hand);
    }
}
