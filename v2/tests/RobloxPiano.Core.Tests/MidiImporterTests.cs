using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using RobloxPiano.Core.Importers;
using RobloxPiano.Core.Music;
using Xunit;

namespace RobloxPiano.Core.Tests;

public class MidiImporterTests
{
    private readonly MidiImporter _importer = new();

    [Fact]
    public void CanImport_ChecksExtensionsAndExistence()
    {
        Assert.Contains(".mid", _importer.SupportedExtensions);
        Assert.Contains(".midi", _importer.SupportedExtensions);

        Assert.False(_importer.CanImport("non_existent_file.mid"));
        Assert.False(_importer.CanImport(""));
    }

    [Fact]
    public void ImportScore_MissingFile_ThrowsFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() => _importer.ImportScore("non_existent_file_12345.mid"));
    }

    [Fact]
    public void ImportFromStream_SimpleQuarterNote_CalculatesTiming()
    {
        // 120 BPM -> Quarter Note = 0.50 seconds
        var midiFile = new MidiFile(new TrackChunk(
            new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)80) { DeltaTime = 0 },
            new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 480 }
        ))
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480)
        };

        using var ms = new MemoryStream();
        midiFile.Write(ms);
        ms.Position = 0;

        var timeline = _importer.ImportFromStream(ms, "SimpleC4");

        Assert.Single(timeline.Notes);
        var note = timeline.Notes[0];
        Assert.Equal(60, note.Pitch);
        Assert.Equal(80, note.Velocity);
        Assert.Equal(0.0, note.StartTime, precision: 4);
        Assert.Equal(0.5, note.EndTime, precision: 4);
        Assert.Equal(0.5, note.Duration, precision: 4);
        Assert.Equal(120.0, timeline.InitialBpm);
        Assert.Equal(HandType.Right, note.Hand); // C4 -> RH by default split
    }

    [Fact]
    public void ImportFromStream_TempoChange_CalculatesSegmentSeconds()
    {
        // Track:
        // t=0: SetTempo 60 BPM (1,000,000 us), NoteOn C4
        // t=480: NoteOff C4 (duration = 1.0 sec at 60 BPM), SetTempo 120 BPM (500,000 us), NoteOn D4
        // t=480: NoteOff D4 (duration = 0.5 sec at 120 BPM)
        var trackChunk = new TrackChunk(
            new SetTempoEvent(1000000) { DeltaTime = 0 },
            new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90) { DeltaTime = 0 },
            new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 480 },
            new SetTempoEvent(500000) { DeltaTime = 0 },
            new NoteOnEvent((SevenBitNumber)62, (SevenBitNumber)90) { DeltaTime = 0 },
            new NoteOffEvent((SevenBitNumber)62, (SevenBitNumber)0) { DeltaTime = 480 }
        );

        var midiFile = new MidiFile(trackChunk)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480)
        };

        using var ms = new MemoryStream();
        midiFile.Write(ms);
        ms.Position = 0;

        var timeline = _importer.ImportFromStream(ms, "TempoChange");

        Assert.Equal(2, timeline.Notes.Count);
        Assert.Equal(60.0, timeline.InitialBpm);

        // Note 1: C4 from 0.0s to 1.0s
        Assert.Equal(0.0, timeline.Notes[0].StartTime, precision: 4);
        Assert.Equal(1.0, timeline.Notes[0].EndTime, precision: 4);

        // Note 2: D4 from 1.0s to 1.5s
        Assert.Equal(1.0, timeline.Notes[1].StartTime, precision: 4);
        Assert.Equal(1.5, timeline.Notes[1].EndTime, precision: 4);
    }

    [Fact]
    public void ImportFromStream_NoteOnVelocityZero_TreatsAsNoteOff()
    {
        var trackChunk = new TrackChunk(
            new NoteOnEvent((SevenBitNumber)64, (SevenBitNumber)100) { DeltaTime = 0 },
            new NoteOnEvent((SevenBitNumber)64, (SevenBitNumber)0) { DeltaTime = 480 }
        );

        var midiFile = new MidiFile(trackChunk)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480)
        };

        using var ms = new MemoryStream();
        midiFile.Write(ms);
        ms.Position = 0;

        var timeline = _importer.ImportFromStream(ms, "NoteOnZero");

        Assert.Single(timeline.Notes);
        Assert.Equal(64, timeline.Notes[0].Pitch);
        Assert.Equal(0.5, timeline.Notes[0].Duration, precision: 4);
    }

    [Fact]
    public void ImportFromStream_RepeatedNoteWithoutNoteOff_ClosesPreviousNote()
    {
        var trackChunk = new TrackChunk(
            new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { DeltaTime = 0 },
            new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)80) { DeltaTime = 240 }, // Retrigger
            new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 240 }
        );

        var midiFile = new MidiFile(trackChunk)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480)
        };

        using var ms = new MemoryStream();
        midiFile.Write(ms);
        ms.Position = 0;

        var timeline = _importer.ImportFromStream(ms, "Retrigger");

        Assert.Equal(2, timeline.Notes.Count);
        Assert.Equal(0.0, timeline.Notes[0].StartTime, precision: 4);
        Assert.Equal(0.25, timeline.Notes[0].EndTime, precision: 4);
        Assert.Equal(0.25, timeline.Notes[1].StartTime, precision: 4);
        Assert.Equal(0.50, timeline.Notes[1].EndTime, precision: 4);
    }

    [Fact]
    public void ImportFromStream_PedalCC64_RecordsPedalEvents()
    {
        var trackChunk = new TrackChunk(
            new ControlChangeEvent((SevenBitNumber)64, (SevenBitNumber)127) { DeltaTime = 240 },
            new ControlChangeEvent((SevenBitNumber)64, (SevenBitNumber)0) { DeltaTime = 480 }
        );

        var midiFile = new MidiFile(trackChunk)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480)
        };

        using var ms = new MemoryStream();
        midiFile.Write(ms);
        ms.Position = 0;

        var timeline = _importer.ImportFromStream(ms, "Pedal");

        Assert.Equal(2, timeline.Pedals.Count);
        Assert.True(timeline.Pedals[0].Down);
        Assert.Equal(0.25, timeline.Pedals[0].Time, precision: 4);
        Assert.False(timeline.Pedals[1].Down);
        Assert.Equal(0.75, timeline.Pedals[1].Time, precision: 4);
    }

    [Fact]
    public void ImportFromStream_MultiTrack_PreservesTrackInfoAndNames()
    {
        var track1 = new TrackChunk(
            new SequenceTrackNameEvent("Treble Melody"),
            new NoteOnEvent((SevenBitNumber)72, (SevenBitNumber)90) { DeltaTime = 0 },
            new NoteOffEvent((SevenBitNumber)72, (SevenBitNumber)0) { DeltaTime = 480 }
        );

        var track2 = new TrackChunk(
            new SequenceTrackNameEvent("Bass Harmony"),
            new NoteOnEvent((SevenBitNumber)48, (SevenBitNumber)70) { DeltaTime = 0 },
            new NoteOffEvent((SevenBitNumber)48, (SevenBitNumber)0) { DeltaTime = 480 }
        );

        var midiFile = new MidiFile(track1, track2)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480)
        };

        using var ms = new MemoryStream();
        midiFile.Write(ms);
        ms.Position = 0;

        var timeline = _importer.ImportFromStream(ms, "MultiTrack");

        Assert.Equal(2, timeline.Notes.Count);
        Assert.Equal("Treble Melody", timeline.TrackNames[0]);
        Assert.Equal("Bass Harmony", timeline.TrackNames[1]);

        Assert.Equal(HandType.Right, timeline.Notes.First(n => n.Pitch == 72).Hand);
        Assert.Equal(HandType.Left, timeline.Notes.First(n => n.Pitch == 48).Hand);
    }
}
