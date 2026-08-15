using RobloxPiano.Core.YouTube;
using Xunit;

namespace RobloxPiano.Core.Tests;

public class YouTubeUrlValidatorTests
{
    [Fact]
    public void YouTubeUrl_Watch_Valid()
    {
        string url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";
        var res = YouTubeUrlValidator.Validate(url);

        Assert.True(res.IsValid);
        Assert.Equal("dQw4w9WgXcQ", res.VideoId);
        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", res.CanonicalUrl);
        Assert.False(res.IsPlaylistOnly);
    }

    [Fact]
    public void YouTubeUrl_ShortLink_Valid()
    {
        string url = "https://youtu.be/dQw4w9WgXcQ";
        var res = YouTubeUrlValidator.Validate(url);

        Assert.True(res.IsValid);
        Assert.Equal("dQw4w9WgXcQ", res.VideoId);
        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", res.CanonicalUrl);
    }

    [Fact]
    public void YouTubeUrl_Shorts_Valid()
    {
        string url = "https://www.youtube.com/shorts/dQw4w9WgXcQ";
        var res = YouTubeUrlValidator.Validate(url);

        Assert.True(res.IsValid);
        Assert.Equal("dQw4w9WgXcQ", res.VideoId);
        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", res.CanonicalUrl);
    }

    [Fact]
    public void YouTubeUrl_MusicYouTube_Valid()
    {
        string url = "https://music.youtube.com/watch?v=dQw4w9WgXcQ";
        var res = YouTubeUrlValidator.Validate(url);

        Assert.True(res.IsValid);
        Assert.Equal("dQw4w9WgXcQ", res.VideoId);
        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", res.CanonicalUrl);
    }

    [Fact]
    public void YouTubeUrl_WithPlaylistParameter_ExtractsSingleVideo()
    {
        string url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=PL1234567890&index=3";
        var res = YouTubeUrlValidator.Validate(url);

        Assert.True(res.IsValid);
        Assert.Equal("dQw4w9WgXcQ", res.VideoId);
        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", res.CanonicalUrl);
        Assert.False(res.IsPlaylistOnly);
    }

    [Fact]
    public void YouTubeUrl_PlaylistOnly_Rejected()
    {
        string url = "https://www.youtube.com/playlist?list=PL1234567890";
        var res = YouTubeUrlValidator.Validate(url);

        Assert.False(res.IsValid);
        Assert.True(res.IsPlaylistOnly);
        Assert.Equal(YouTubeError.PlaylistUnsupported, res.ErrorMessage);
    }

    [Fact]
    public void YouTubeUrl_NonYouTube_Rejected()
    {
        string url = "https://vimeo.com/123456789";
        var res = YouTubeUrlValidator.Validate(url);

        Assert.False(res.IsValid);
        Assert.Equal(YouTubeError.UnsupportedHost, res.ErrorMessage);
    }

    [Fact]
    public void YouTubeUrl_Localhost_Rejected()
    {
        string url = "http://localhost/watch?v=dQw4w9WgXcQ";
        var res = YouTubeUrlValidator.Validate(url);

        Assert.False(res.IsValid);
        Assert.Equal(YouTubeError.UnsupportedHost, res.ErrorMessage);
    }

    [Fact]
    public void YouTubeUrl_FileScheme_Rejected()
    {
        string url = "file:///C:/songs/audio.wav";
        var res = YouTubeUrlValidator.Validate(url);

        Assert.False(res.IsValid);
        Assert.Equal(YouTubeError.InvalidUrl, res.ErrorMessage);
    }

    [Fact]
    public void YouTubeUrl_EmbeddedCredentials_Rejected()
    {
        string url = "https://user:password@www.youtube.com/watch?v=dQw4w9WgXcQ";
        var res = YouTubeUrlValidator.Validate(url);

        Assert.False(res.IsValid);
        Assert.Equal(YouTubeError.InvalidUrl, res.ErrorMessage);
    }

    [Fact]
    public void YouTubeUrl_TrackingParameters_Removed()
    {
        string url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ&si=abcdef123456&utm_source=share&utm_medium=link";
        var res = YouTubeUrlValidator.Validate(url);

        Assert.True(res.IsValid);
        Assert.Equal("dQw4w9WgXcQ", res.VideoId);
        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", res.CanonicalUrl);
    }

    [Fact]
    public void YouTubeUrl_UnicodeGarbage_Rejected()
    {
        string url = "https://www.youtube.com/watch?v=한글아이디테스트";
        var res = YouTubeUrlValidator.Validate(url);

        Assert.False(res.IsValid);
        Assert.Equal(YouTubeError.InvalidUrl, res.ErrorMessage);
    }
}
