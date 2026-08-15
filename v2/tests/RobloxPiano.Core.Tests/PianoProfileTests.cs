using RobloxPiano.Core.Importing;
using RobloxPiano.Core.Music;
using RobloxPiano.Core.Piano;
using Xunit;

namespace RobloxPiano.Core.Tests;

public class PianoProfileTests
{
    [Fact]
    public void Load61KeyProfile_LoadsSuccessfully()
    {
        var profile = PianoProfileLoader.Load61KeyProfile();
        Assert.NotNull(profile);
        Assert.Equal(36, profile.MinPitch);
        Assert.Equal(96, profile.MaxPitch);
        Assert.True(profile.Keys.Count >= 61);
        Assert.True(profile.Keys.ContainsKey(60)); // Middle C (C4)
    }

    [Fact]
    public void Load88KeyProfile_LoadsSuccessfully()
    {
        var profile = PianoProfileLoader.Load88KeyProfile();
        Assert.NotNull(profile);
        Assert.Equal(21, profile.MinPitch);
        Assert.Equal(108, profile.MaxPitch);
        Assert.True(profile.Keys.Count >= 88);
        Assert.True(profile.Keys.ContainsKey(21)); // A0
        Assert.True(profile.Keys.ContainsKey(108)); // C8
    }

    [Fact]
    public void LoadProfileFromJson_MigratesLegacyShiftTrue()
    {
        string syntheticJson = """
        {
            "name": "Legacy Test Profile",
            "min_pitch": 36,
            "max_pitch": 96,
            "keys": {
                "60": {
                    "char": "C",
                    "physical_key": "c",
                    "shift": true,
                    "name": "C4"
                },
                "62": {
                    "char": "d",
                    "physical_key": "d",
                    "modifiers": ["CTRL"],
                    "name": "D4"
                }
            }
        }
        """;

        var profile = PianoProfileLoader.LoadProfileFromJson(syntheticJson);
        Assert.Equal("Legacy Test Profile", profile.Name);
        Assert.Contains("SHIFT", profile.Keys[60].Modifiers);
        Assert.DoesNotContain("SHIFT", profile.Keys[62].Modifiers);
        Assert.Contains("CTRL", profile.Keys[62].Modifiers);
    }
}

public class RobloxPianoMapperTests
{
    [Fact]
    public void Mapper_MapPitch_ReturnsCorrectKeyMapping()
    {
        var mapper = new RobloxPianoMapper();
        var mappingC4 = mapper.MapPitch(60);
        Assert.NotNull(mappingC4);
        Assert.Equal(60, mappingC4.Pitch);
        Assert.False(string.IsNullOrEmpty(mappingC4.Char));
    }

    [Fact]
    public void Mapper_MapNoteEvent_ReturnsCorrectMapping()
    {
        var mapper = new RobloxPianoMapper();
        var note = new NoteEvent(60, 0, 1);
        var mapping = mapper.MapNoteEvent(note);
        Assert.NotNull(mapping);
        Assert.Equal(60, mapping.Pitch);
    }

    [Fact]
    public void Mapper_CanPlay_ReturnsTrueForMappedPitches()
    {
        var mapper = new RobloxPianoMapper(PianoProfileLoader.Load61KeyProfile());
        Assert.True(mapper.CanPlay(36));
        Assert.True(mapper.CanPlay(96));
        Assert.False(mapper.CanPlay(20)); // Out of 61-key
        Assert.False(mapper.CanPlay(105)); // Out of 61-key
    }

    [Fact]
    public void Mapper_GetByChar_ReturnsKeyMapping()
    {
        var mapper = new RobloxPianoMapper();
        var kmC4 = mapper.MapPitch(60);
        Assert.NotNull(kmC4);

        var lookedUp = mapper.GetByChar(kmC4.Char);
        Assert.NotNull(lookedUp);
        Assert.Equal(kmC4.Pitch, lookedUp.Pitch);
    }

    [Fact]
    public void Mapper_SetProfile_SwapsActiveProfile()
    {
        var mapper = new RobloxPianoMapper(PianoProfileLoader.Load61KeyProfile());
        Assert.Equal(36, mapper.MinPitch);

        mapper.SetProfile(PianoProfileLoader.Load88KeyProfile());
        Assert.Equal(21, mapper.MinPitch);
        Assert.True(mapper.CanPlay(21));
    }
}

public class ImportValidationProfileTests
{
    [Fact]
    public void ImportValidation_DefaultProfile_Is88Key()
    {
        var timeline = new MusicTimeline("Default 88-key");
        timeline.Notes.Add(new NoteEvent(21, 0, 1)); // A0
        timeline.Notes.Add(new NoteEvent(108, 1, 2)); // C8

        var res = ImportTimelineValidator.Validate(timeline);
        Assert.True(res.IsValid);
        Assert.Equal(2, res.PlayableNotes);
        Assert.Equal(0, res.OutOfRangeNotes);
    }

    [Fact]
    public void ImportValidation_88Key_Pitch21And108Playable()
    {
        var timeline = new MusicTimeline("88-key test");
        timeline.Notes.Add(new NoteEvent(21, 0, 1));
        timeline.Notes.Add(new NoteEvent(60, 0.5, 1.5));
        timeline.Notes.Add(new NoteEvent(108, 1, 2));

        var profile88 = PianoProfileLoader.Load88KeyProfile();
        var res = ImportTimelineValidator.Validate(timeline, profile88);
        Assert.True(res.IsValid);
        Assert.Equal(3, res.PlayableNotes);
        Assert.Equal(0, res.OutOfRangeNotes);
    }

    [Fact]
    public void ImportValidation_61Key_Pitch21OutOfRange_Pitch36Playable()
    {
        var timeline = new MusicTimeline("61-key test");
        timeline.Notes.Add(new NoteEvent(21, 0, 1));  // A0 (out of 61-key)
        timeline.Notes.Add(new NoteEvent(36, 0, 1));  // C2 (playable in 61-key)
        timeline.Notes.Add(new NoteEvent(96, 0, 1));  // C7 (playable in 61-key)
        timeline.Notes.Add(new NoteEvent(108, 1, 2)); // C8 (out of 61-key)

        var profile61 = PianoProfileLoader.Load61KeyProfile();
        var res = ImportTimelineValidator.Validate(timeline, profile61);
        Assert.True(res.IsValid);
        Assert.Equal(2, res.PlayableNotes); // 36, 96
        Assert.Equal(2, res.OutOfRangeNotes); // 21, 108
    }
}
