using RobloxPiano.Core.Audio;
using RobloxPiano.Infrastructure.Audio;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class FfprobeParserTests
{
    [Fact]
    public void FfprobeParser_ValidAudioJson_ParsesMetadata()
    {
        string json = @"
{
    ""streams"": [
        {
            ""codec_type"": ""audio"",
            ""codec_name"": ""mp3"",
            ""channels"": 2,
            ""sample_rate"": ""44100"",
            ""bit_rate"": ""320000""
        }
    ],
    ""format"": {
        ""format_name"": ""mp3"",
        ""duration"": ""185.5"",
        ""size"": ""7420000"",
        ""bit_rate"": ""320000"",
        ""tags"": {
            ""title"": ""Piano Recital"",
            ""artist"": ""Chopin""
        }
    }
}";

        var result = FfprobeMetadataReader.ParseFfprobeJson(json, @"C:\Music\recital.mp3");

        Assert.True(result.IsValid);
        Assert.NotNull(result.Metadata);
        Assert.Equal("mp3", result.Metadata.CodecName);
        Assert.Equal(2, result.Metadata.Channels);
        Assert.Equal(44100, result.Metadata.SampleRate);
        Assert.Equal(185.5, result.Metadata.DurationSeconds, precision: 1);
        Assert.Equal(320000, result.Metadata.BitRate);
        Assert.Equal(7420000, result.Metadata.FileSizeBytes);
        Assert.Equal(1, result.Metadata.AudioStreamCount);
        Assert.Equal("Piano Recital", result.Metadata.Title);
        Assert.Equal("Chopin", result.Metadata.Artist);
    }

    [Fact]
    public void FfprobeParser_NoAudioStream_Rejects()
    {
        string json = @"
{
    ""streams"": [
        {
            ""codec_type"": ""video"",
            ""codec_name"": ""h264""
        }
    ],
    ""format"": {
        ""format_name"": ""mov,mp4"",
        ""duration"": ""60.0"",
        ""size"": ""1000000""
    }
}";

        var result = FfprobeMetadataReader.ParseFfprobeJson(json, @"C:\Music\video_only.mp4");

        Assert.False(result.IsValid);
        Assert.Equal(AudioError.NoAudioStream, result.ErrorMessage);
    }

    [Fact]
    public void FfprobeParser_InvalidDuration_Rejects()
    {
        string json = @"
{
    ""streams"": [
        {
            ""codec_type"": ""audio"",
            ""codec_name"": ""pcm_s16le"",
            ""channels"": 1,
            ""sample_rate"": ""22050""
        }
    ],
    ""format"": {
        ""format_name"": ""wav"",
        ""duration"": ""0.0"",
        ""size"": ""100""
    }
}";

        var result = FfprobeMetadataReader.ParseFfprobeJson(json, @"C:\Music\zero_length.wav");

        Assert.False(result.IsValid);
        Assert.Equal(AudioError.InvalidMedia, result.ErrorMessage);
    }

    [Fact]
    public void FfprobeParser_MultipleStreams_SelectsFirstAudio()
    {
        string json = @"
{
    ""streams"": [
        {
            ""codec_type"": ""video"",
            ""codec_name"": ""mjpeg""
        },
        {
            ""codec_type"": ""audio"",
            ""codec_name"": ""aac"",
            ""channels"": 2,
            ""sample_rate"": ""48000"",
            ""bit_rate"": ""192000""
        },
        {
            ""codec_type"": ""audio"",
            ""codec_name"": ""mp3"",
            ""channels"": 1,
            ""sample_rate"": ""22050""
        }
    ],
    ""format"": {
        ""format_name"": ""mov,mp4,m4a"",
        ""duration"": ""120.0"",
        ""size"": ""2500000""
    }
}";

        var result = FfprobeMetadataReader.ParseFfprobeJson(json, @"C:\Music\multitrack.m4a");

        Assert.True(result.IsValid);
        Assert.NotNull(result.Metadata);
        Assert.Equal("aac", result.Metadata.CodecName);
        Assert.Equal(2, result.Metadata.Channels);
        Assert.Equal(48000, result.Metadata.SampleRate);
        Assert.Equal(2, result.Metadata.AudioStreamCount);
    }

    [Fact]
    public void FfprobeParser_MissingOptionalTags_Succeeds()
    {
        string json = @"
{
    ""streams"": [
        {
            ""codec_type"": ""audio"",
            ""codec_name"": ""flac"",
            ""channels"": 2,
            ""sample_rate"": ""96000""
        }
    ],
    ""format"": {
        ""format_name"": ""flac"",
        ""duration"": ""45.2"",
        ""size"": ""15000000""
    }
}";

        var result = FfprobeMetadataReader.ParseFfprobeJson(json, @"C:\Music\no_tags.flac");

        Assert.True(result.IsValid);
        Assert.NotNull(result.Metadata);
        Assert.Null(result.Metadata.Title);
        Assert.Null(result.Metadata.Artist);
        Assert.Equal("flac", result.Metadata.CodecName);
        Assert.Equal(96000, result.Metadata.SampleRate);
        Assert.Equal(45.2, result.Metadata.DurationSeconds, precision: 1);
    }
}
