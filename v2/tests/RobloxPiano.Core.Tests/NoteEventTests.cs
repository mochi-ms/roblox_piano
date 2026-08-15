using RobloxPiano.Core.Music;
using Xunit;

namespace RobloxPiano.Core.Tests;

public class NoteEventTests
{
    [Fact]
    public void Duration_CalculatesCorrectly()
    {
        var note = new NoteEvent(60, 1.0, 2.5);
        Assert.Equal(1.5, note.Duration, precision: 5);
    }

    [Fact]
    public void Duration_ClampsToMinimum()
    {
        var note = new NoteEvent(60, 1.0, 1.002);
        Assert.Equal(0.01, note.Duration, precision: 5);
    }

    [Fact]
    public void IsInRange_ChecksBoundaries()
    {
        var noteC2 = new NoteEvent(36, 0, 1);
        var noteC7 = new NoteEvent(96, 0, 1);
        var noteLow = new NoteEvent(35, 0, 1);
        var noteHigh = new NoteEvent(97, 0, 1);

        Assert.True(noteC2.IsInRange(36, 96));
        Assert.True(noteC7.IsInRange(36, 96));
        Assert.False(noteLow.IsInRange(36, 96));
        Assert.False(noteHigh.IsInRange(36, 96));
    }
}
