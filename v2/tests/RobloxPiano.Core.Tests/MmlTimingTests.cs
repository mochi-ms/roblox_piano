using Melanchall.DryWetMidi.Core;
using RobloxPiano.Core.Importers;
using Xunit;

namespace RobloxPiano.Core.Tests;

public class MmlTimingTests
{
    private readonly MmlImporter _importer = new();

    [Fact]
    public void Case1_NumericNoteBeforeLengthCommand_MaintainsForwardOnlySemantics()
    {
        // CASE 1: MML@T150L16N58L8GG;
        // N58 uses L16 (120 ticks) -> L8 takes effect for future notes -> G (240 ticks) -> G (240 ticks) -> total 600 ticks
        string mml = "MML@T150L16N58L8GG;";
        var (mid, meta) = _importer.ParseToMidi(mml);
        var track = (TrackChunk)mid.Chunks[0];

        var events = track.Events.Where(e => e is NoteOnEvent or NoteOffEvent).ToList();
        Assert.Equal(6, events.Count);

        // N58 on/off
        var nOn1 = (NoteOnEvent)events[0];
        var nOff1 = (NoteOffEvent)events[1];
        Assert.Equal(58, (int)nOn1.NoteNumber);
        Assert.Equal(0, nOn1.DeltaTime);
        Assert.Equal(58, (int)nOff1.NoteNumber);
        Assert.Equal(120, nOff1.DeltaTime);

        // G on/off (pitch = 67)
        var nOn2 = (NoteOnEvent)events[2];
        var nOff2 = (NoteOffEvent)events[3];
        Assert.Equal(67, (int)nOn2.NoteNumber);
        Assert.Equal(0, nOn2.DeltaTime);
        Assert.Equal(67, (int)nOff2.NoteNumber);
        Assert.Equal(240, nOff2.DeltaTime);

        // G on/off (pitch = 67)
        var nOn3 = (NoteOnEvent)events[4];
        var nOff3 = (NoteOffEvent)events[5];
        Assert.Equal(67, (int)nOn3.NoteNumber);
        Assert.Equal(0, nOn3.DeltaTime);
        Assert.Equal(67, (int)nOff3.NoteNumber);
        Assert.Equal(240, nOff3.DeltaTime);

        long totalTicks = track.Events.Sum(e => e.DeltaTime);
        Assert.Equal(600, totalTicks);
    }

    [Fact]
    public void Case2_RegularNoteBeforeLengthCommand_SnapshotsCurrentLength()
    {
        // CASE 2: MML@T120L8CL16D;
        string mml = "MML@T120L8CL16D;";
        var (mid, _) = _importer.ParseToMidi(mml);
        var track = (TrackChunk)mid.Chunks[0];
        var events = track.Events.Where(e => e is NoteOnEvent or NoteOffEvent).ToList();

        var cOff = (NoteOffEvent)events[1];
        var dOff = (NoteOffEvent)events[3];

        Assert.Equal(60, (int)cOff.NoteNumber);
        Assert.Equal(240, cOff.DeltaTime); // C uses L8 = 240
        Assert.Equal(62, (int)dOff.NoteNumber);
        Assert.Equal(120, dOff.DeltaTime); // D uses L16 = 120
    }

    [Fact]
    public void Case3_DefaultLengthAppliesForwardOnly()
    {
        // CASE 3: MML@T120L16FL8G;
        string mml = "MML@T120L16FL8G;";
        var (mid, _) = _importer.ParseToMidi(mml);
        var track = (TrackChunk)mid.Chunks[0];
        var events = track.Events.Where(e => e is NoteOnEvent or NoteOffEvent).ToList();

        Assert.Equal(120, events[1].DeltaTime); // F note_off = 120
        Assert.Equal(240, events[3].DeltaTime); // G note_off = 240
    }

    [Fact]
    public void Case4_ExplicitLengthDoesNotChangeDefault()
    {
        // CASE 4: MML@T120L8C16D;
        string mml = "MML@T120L8C16D;";
        var (mid, _) = _importer.ParseToMidi(mml);
        var track = (TrackChunk)mid.Chunks[0];
        var events = track.Events.Where(e => e is NoteOnEvent or NoteOffEvent).ToList();

        Assert.Equal(120, events[1].DeltaTime); // C16 explicit duration = 120
        Assert.Equal(240, events[3].DeltaTime); // D default L8 = 240
    }

    [Fact]
    public void Case5_TieUsesSegmentLengths()
    {
        // CASE 5: MML@T120L8C&C;
        string mml = "MML@T120L8C&C;";
        var (mid, _) = _importer.ParseToMidi(mml);
        var track = (TrackChunk)mid.Chunks[0];
        var events = track.Events.Where(e => e is NoteOnEvent or NoteOffEvent).ToList();

        // Single continuous note C of 480 ticks
        Assert.Equal(2, events.Count);
        var cOn = (NoteOnEvent)events[0];
        var cOff = (NoteOffEvent)events[1];
        Assert.Equal(60, (int)cOn.NoteNumber);
        Assert.Equal(0, cOn.DeltaTime);
        Assert.Equal(60, (int)cOff.NoteNumber);
        Assert.Equal(480, cOff.DeltaTime);
    }

    [Fact]
    public void Case6_LCommandDoesNotModifyPreviousNote()
    {
        string mml = "MML@T120L4CL1";
        var (mid, _) = _importer.ParseToMidi(mml);
        var track = (TrackChunk)mid.Chunks[0];
        var events = track.Events.Where(e => e is NoteOnEvent or NoteOffEvent).ToList();

        Assert.Equal(480, events[1].DeltaTime); // C remains 480 ticks (L4), not 1920 (L1)
    }

    [Fact]
    public void Case7_DefaultLengthStateTransition()
    {
        string mml = "MML@T120L4CL8DL16EL32F";
        var (mid, _) = _importer.ParseToMidi(mml);
        var track = (TrackChunk)mid.Chunks[0];
        var events = track.Events.Where(e => e is NoteOnEvent or NoteOffEvent).ToList();

        Assert.Equal(480, events[1].DeltaTime); // C (L4)
        Assert.Equal(240, events[3].DeltaTime); // D (L8)
        Assert.Equal(120, events[5].DeltaTime); // E (L16)
        Assert.Equal(60, events[7].DeltaTime);  // F (L32)
    }

    [Fact]
    public void Case8_DefaultLengthMultipleTransitions()
    {
        string mml = "MML@T120L4.CL8.DL16.E";
        var (mid, _) = _importer.ParseToMidi(mml);
        var track = (TrackChunk)mid.Chunks[0];
        var events = track.Events.Where(e => e is NoteOnEvent or NoteOffEvent).ToList();

        Assert.Equal(720, events[1].DeltaTime); // L4. = 480 + 240 = 720
        Assert.Equal(360, events[3].DeltaTime); // L8. = 240 + 120 = 360
        Assert.Equal(180, events[5].DeltaTime); // L16. = 120 + 60 = 180
    }

    [Fact]
    public void Case9_NoteDurationSnapshot()
    {
        string mml = "MML@T120L8C>D<L16EF";
        var (mid, _) = _importer.ParseToMidi(mml);
        var track = (TrackChunk)mid.Chunks[0];
        var events = track.Events.Where(e => e is NoteOnEvent or NoteOffEvent).ToList();

        Assert.Equal(240, events[1].DeltaTime); // C = 240
        Assert.Equal(240, events[3].DeltaTime); // D = 240
        Assert.Equal(120, events[5].DeltaTime); // E = 120
        Assert.Equal(120, events[7].DeltaTime); // F = 120
    }

    [Fact]
    public void Case10_RestUsesCurrentDefaultLength()
    {
        string mml = "MML@T120L8CRCL16RD";
        var (mid, _) = _importer.ParseToMidi(mml);
        var track = (TrackChunk)mid.Chunks[0];
        var events = track.Events.Where(e => e is NoteOnEvent or NoteOffEvent).ToList();

        Assert.Equal(0, events[0].DeltaTime);   // C on
        Assert.Equal(240, events[1].DeltaTime); // C off
        Assert.Equal(240, events[2].DeltaTime); // R8 gap -> next C on at +240
        Assert.Equal(240, events[3].DeltaTime); // C off
        Assert.Equal(120, events[4].DeltaTime); // R16 gap -> next D on at +120
        Assert.Equal(120, events[5].DeltaTime); // D off
    }

    [Fact]
    public void Case11_LilacFirstLengthTransition()
    {
        string snippet = "MML@T150L16N58L8G<G>GF4D+D<A+R2B+L16;";
        var (mid, _) = _importer.ParseToMidi(snippet);
        var track = (TrackChunk)mid.Chunks[0];
        var events = track.Events.Where(e => e is NoteOnEvent or NoteOffEvent).ToList();

        // N58 duration must be 120 (L16)
        var n58On = (NoteOnEvent)events[0];
        var n58Off = (NoteOffEvent)events[1];
        Assert.Equal(58, (int)n58On.NoteNumber);
        Assert.Equal(58, (int)n58Off.NoteNumber);
        Assert.Equal(120, n58Off.DeltaTime);

        // Following G duration must be 240 (L8)
        var gOn = (NoteOnEvent)events[2];
        var gOff = (NoteOffEvent)events[3];
        Assert.Equal(67, (int)gOn.NoteNumber);
        Assert.Equal(0, gOn.DeltaTime);
        Assert.Equal(67, (int)gOff.NoteNumber);
        Assert.Equal(240, gOff.DeltaTime);
    }
}
