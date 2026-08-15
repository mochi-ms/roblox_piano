using RobloxPiano.Core.Audio;
using Xunit;

namespace RobloxPiano.Core.Tests;

public class AudioDomainTests
{
    [Theory]
    [InlineData("song.mp3", AudioSourceType.Mp3, "MP3")]
    [InlineData("audio.wav", AudioSourceType.Wav, "WAV")]
    [InlineData("track.m4a", AudioSourceType.M4a, "M4A")]
    [InlineData("music.flac", AudioSourceType.Flac, "FLAC")]
    [InlineData("sound.aac", AudioSourceType.Aac, "AAC")]
    [InlineData("clip.ogg", AudioSourceType.Ogg, "OGG")]
    [InlineData("video.mp4", AudioSourceType.Unknown, "알 수 없음")]
    [InlineData("doc.pdf", AudioSourceType.Unknown, "알 수 없음")]
    public void AudioSourceTypeExtensions_MapsExtensionsAndFriendlyNames(string path, AudioSourceType expectedType, string expectedFriendly)
    {
        var type = AudioSourceTypeExtensions.FromExtension(path);
        Assert.Equal(expectedType, type);
        Assert.Equal(expectedFriendly, type.ToFriendlyString());
    }

    [Fact]
    public void AudioIngestResult_Successful_CreatesValidContract()
    {
        var meta = new AudioMetadata(@"C:\audio\song.mp3", "mp3", "mp3", 120.5, 44100, 2, 320000, 5000000, 1, "Song Title", "Artist Name");
        var result = AudioIngestResult.Successful("job_01", @"C:\audio\song.mp3", @"C:\workspace\job_01\normalized.wav", meta);

        Assert.True(result.Success);
        Assert.Equal("job_01", result.JobId);
        Assert.Equal(@"C:\audio\song.mp3", result.SourcePath);
        Assert.Equal(@"C:\workspace\job_01\normalized.wav", result.NormalizedAudioPath);
        Assert.NotNull(result.Metadata);
        Assert.Equal(120.5, result.Metadata.DurationSeconds);
        Assert.Equal("Song Title", result.Metadata.Title);
        Assert.Equal("Artist Name", result.Metadata.Artist);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void AudioIngestResult_Failed_CreatesFailureContract()
    {
        var result = AudioIngestResult.Failed(@"C:\audio\corrupt.mp3", AudioError.InvalidMedia, "CORRUPT", "job_02");

        Assert.False(result.Success);
        Assert.Equal("job_02", result.JobId);
        Assert.Equal(AudioError.InvalidMedia, result.ErrorMessage);
        Assert.Equal("CORRUPT", result.ErrorCode);
        Assert.Null(result.NormalizedAudioPath);
    }
}
