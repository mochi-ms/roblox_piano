using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using RobloxPiano.Core.Importing;
using Xunit;

namespace RobloxPiano.Core.Tests;

public class ImportDetectorTests : IDisposable
{
    private readonly string _tempDir;

    public ImportDetectorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "rp_detector_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    private string CreateValidMidiFile(string filename = "valid.mid")
    {
        string path = Path.Combine(_tempDir, filename);
        var midiFile = new MidiFile(new TrackChunk(
            new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)64) { DeltaTime = 0 },
            new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 480 }
        ));
        midiFile.Write(path, true);
        return path;
    }

    [Fact]
    public void ImportDetector_MidExtensionAndHeader_DetectsMidi()
    {
        string midPath = CreateValidMidiFile("test_song.mid");
        string midiPath = CreateValidMidiFile("test_song.midi");

        var (type1, err1) = ImportFileDetector.Detect(midPath);
        var (type2, err2) = ImportFileDetector.Detect(midiPath);

        Assert.Equal(ImportSourceType.Midi, type1);
        Assert.Null(err1);

        Assert.Equal(ImportSourceType.Midi, type2);
        Assert.Null(err2);
    }

    [Fact]
    public void ImportDetector_MidiExtensionButInvalidHeader_Rejects()
    {
        string fakeMidiPath = Path.Combine(_tempDir, "fake.mid");
        File.WriteAllText(fakeMidiPath, "This is not a real MIDI file header");

        var (type, err) = ImportFileDetector.Detect(fakeMidiPath);

        Assert.Equal(ImportSourceType.Unknown, type);
        Assert.Equal(ImportError.CorruptMidi, err);
    }

    [Fact]
    public void ImportDetector_MmlExtension_DetectsMml()
    {
        string mmlPath = Path.Combine(_tempDir, "song.mml");
        File.WriteAllText(mmlPath, "MML@T150L16N58L8GG;");

        var (type, err) = ImportFileDetector.Detect(mmlPath);

        Assert.Equal(ImportSourceType.Mml, type);
        Assert.Null(err);
    }

    [Fact]
    public void ImportDetector_TxtWithValidMml_DetectsMml()
    {
        string txtMmlPath = Path.Combine(_tempDir, "mml_song.txt");
        File.WriteAllText(txtMmlPath, "MML@t120l4cdefgab;");

        var (type, err) = ImportFileDetector.Detect(txtMmlPath);

        Assert.Equal(ImportSourceType.Mml, type);
        Assert.Null(err);
    }

    [Fact]
    public void ImportDetector_TxtWithoutMml_Rejects()
    {
        string plainTxtPath = Path.Combine(_tempDir, "notes.txt");
        File.WriteAllText(plainTxtPath, "Shopping list:\n1. Apples\n2. Milk\n3. Bread");

        var (type, err) = ImportFileDetector.Detect(plainTxtPath);

        Assert.Equal(ImportSourceType.Unknown, type);
        Assert.Equal(ImportError.InvalidMml, err);
    }

    [Fact]
    public void ImportDetector_UnsupportedExtension_Rejects()
    {
        string mp3Path = Path.Combine(_tempDir, "audio.mp3");
        File.WriteAllText(mp3Path, "dummy audio bytes");

        string wavPath = Path.Combine(_tempDir, "audio.wav");
        File.WriteAllText(wavPath, "dummy wav bytes");

        var (type1, err1) = ImportFileDetector.Detect(mp3Path);
        var (type2, err2) = ImportFileDetector.Detect(wavPath);

        Assert.Equal(ImportSourceType.Unknown, type1);
        Assert.Equal(ImportError.UnsupportedFormat, err1);

        Assert.Equal(ImportSourceType.Unknown, type2);
        Assert.Equal(ImportError.UnsupportedFormat, err2);
    }
}
