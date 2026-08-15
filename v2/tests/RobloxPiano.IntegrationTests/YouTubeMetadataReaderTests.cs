using RobloxPiano.Core.YouTube;
using RobloxPiano.Infrastructure.Audio;
using RobloxPiano.Infrastructure.YouTube;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class YouTubeMetadataReaderTests
{
    private class MockYtDlpRunner : IYtDlpProcessRunner
    {
        private readonly Func<string, IReadOnlyList<string>, ProcessExecutionResult> _handler;

        public MockYtDlpRunner(Func<string, IReadOnlyList<string>, ProcessExecutionResult> handler)
        {
            _handler = handler;
        }

        public Task<ProcessExecutionResult> RunProcessAsync(string executablePath, IReadOnlyList<string> arguments, Action<string>? onStdOutLine = null, Action<string>? onStdErrLine = null, TimeSpan? timeout = null, CancellationToken ct = default)
        {
            return Task.FromResult(_handler(executablePath, arguments));
        }
    }

    private class MockToolLocator : IYtDlpToolLocator
    {
        public Task<YouTubeToolStatus> LocateAsync(string? explicitPath = null, CancellationToken ct = default)
        {
            return Task.FromResult(YouTubeToolStatus.Available(@"C:\tools\yt-dlp.exe", "2024.08.06"));
        }
    }

    [Fact]
    public async Task YouTubeMetadata_ValidJson_Parses()
    {
        string json = @"
{
    ""id"": ""dQw4w9WgXcQ"",
    ""title"": ""Rick Astley - Never Gonna Give You Up"",
    ""duration"": 213,
    ""channel"": ""RickAstleyVEVO"",
    ""webpage_url"": ""https://www.youtube.com/watch?v=dQw4w9WgXcQ"",
    ""thumbnail"": ""https://i.ytimg.com/vi/dQw4w9WgXcQ/hqdefault.jpg"",
    ""is_live"": false,
    ""extractor"": ""youtube""
}";

        var runner = new MockYtDlpRunner((_, _) => ProcessExecutionResult.Success(json));
        var service = new YouTubeIngestionService(
            toolLocator: new MockToolLocator(),
            processRunner: runner
        );

        var meta = await service.ProbeMetadataAsync("https://www.youtube.com/watch?v=dQw4w9WgXcQ");

        Assert.Equal("dQw4w9WgXcQ", meta.Id);
        Assert.Equal("Rick Astley - Never Gonna Give You Up", meta.Title);
        Assert.Equal(213, meta.DurationSeconds);
        Assert.Equal("RickAstleyVEVO", meta.Channel);
        Assert.False(meta.IsLive);
    }

    [Fact]
    public async Task YouTubeMetadata_MissingOptionalFields_Succeeds()
    {
        string json = @"
{
    ""id"": ""dQw4w9WgXcQ"",
    ""duration"": 120
}";

        var runner = new MockYtDlpRunner((_, _) => ProcessExecutionResult.Success(json));
        var service = new YouTubeIngestionService(
            toolLocator: new MockToolLocator(),
            processRunner: runner
        );

        var meta = await service.ProbeMetadataAsync("https://www.youtube.com/watch?v=dQw4w9WgXcQ");

        Assert.Equal("dQw4w9WgXcQ", meta.Id);
        Assert.Equal("dQw4w9WgXcQ", meta.Title); // fallback to id
        Assert.Equal("YouTube", meta.Channel); // fallback to YouTube
        Assert.Equal(120, meta.DurationSeconds);
    }

    [Fact]
    public async Task YouTubeMetadata_IdMismatch_Rejected()
    {
        string json = @"
{
    ""id"": ""different_video_id"",
    ""title"": ""Some other video"",
    ""duration"": 100
}";

        var runner = new MockYtDlpRunner((_, _) => ProcessExecutionResult.Success(json));
        var service = new YouTubeIngestionService(
            toolLocator: new MockToolLocator(),
            processRunner: runner
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ProbeMetadataAsync("https://www.youtube.com/watch?v=dQw4w9WgXcQ"));

        Assert.Contains(YouTubeError.VideoIdMismatch, ex.Message);
    }

    [Fact]
    public async Task YouTubeMetadata_Live_Rejected()
    {
        string json = @"
{
    ""id"": ""dQw4w9WgXcQ"",
    ""title"": ""Live Stream"",
    ""duration"": 300,
    ""is_live"": true
}";

        var runner = new MockYtDlpRunner((_, _) => ProcessExecutionResult.Success(json));
        var service = new YouTubeIngestionService(
            toolLocator: new MockToolLocator(),
            processRunner: runner
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ProbeMetadataAsync("https://www.youtube.com/watch?v=dQw4w9WgXcQ"));

        Assert.Contains(YouTubeError.LiveUnsupported, ex.Message);
    }

    [Fact]
    public async Task YouTubeMetadata_Upcoming_Rejected()
    {
        string json = @"
{
    ""id"": ""dQw4w9WgXcQ"",
    ""title"": ""Upcoming Premier"",
    ""duration"": 300,
    ""live_status"": ""is_upcoming""
}";

        var runner = new MockYtDlpRunner((_, _) => ProcessExecutionResult.Success(json));
        var service = new YouTubeIngestionService(
            toolLocator: new MockToolLocator(),
            processRunner: runner
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ProbeMetadataAsync("https://www.youtube.com/watch?v=dQw4w9WgXcQ"));

        Assert.Contains(YouTubeError.LiveUnsupported, ex.Message);
    }

    [Fact]
    public async Task YouTubeMetadata_TooLong_Rejected()
    {
        string json = @"
{
    ""id"": ""dQw4w9WgXcQ"",
    ""title"": ""Very Long Concert"",
    ""duration"": 1801
}";

        var runner = new MockYtDlpRunner((_, _) => ProcessExecutionResult.Success(json));
        var service = new YouTubeIngestionService(
            toolLocator: new MockToolLocator(),
            processRunner: runner
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ProbeMetadataAsync("https://www.youtube.com/watch?v=dQw4w9WgXcQ"));

        Assert.Contains(YouTubeError.TooLong, ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task YouTubeMetadata_InvalidDuration_Rejected(double invalidDuration)
    {
        string json = $@"
{{
    ""id"": ""dQw4w9WgXcQ"",
    ""title"": ""Zero Duration"",
    ""duration"": {invalidDuration}
}}";

        var runner = new MockYtDlpRunner((_, _) => ProcessExecutionResult.Success(json));
        var service = new YouTubeIngestionService(
            toolLocator: new MockToolLocator(),
            processRunner: runner
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ProbeMetadataAsync("https://www.youtube.com/watch?v=dQw4w9WgXcQ"));

        Assert.Contains(YouTubeError.DurationUnknown, ex.Message);
    }

    [Fact]
    public async Task YouTubeMetadata_LoginRequired_MapsFriendlyError()
    {
        var runner = new MockYtDlpRunner((_, _) =>
            ProcessExecutionResult.Failure(1, "ERROR: Private video. Sign in if you've been granted access to this video"));
        var service = new YouTubeIngestionService(
            toolLocator: new MockToolLocator(),
            processRunner: runner
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ProbeMetadataAsync("https://www.youtube.com/watch?v=dQw4w9WgXcQ"));

        Assert.Contains(YouTubeError.LoginRequired, ex.Message);
    }

    [Fact]
    public async Task YouTubeMetadata_MalformedJson_FailsSafely()
    {
        var runner = new MockYtDlpRunner((_, _) => ProcessExecutionResult.Success("{not-valid-json"));
        var service = new YouTubeIngestionService(
            toolLocator: new MockToolLocator(),
            processRunner: runner
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ProbeMetadataAsync("https://www.youtube.com/watch?v=dQw4w9WgXcQ"));

        Assert.Contains(YouTubeError.MetadataFailed, ex.Message);
    }
}
