using RobloxPiano.Core.Audio;
using RobloxPiano.Core.YouTube;
using RobloxPiano.Infrastructure.Audio;
using RobloxPiano.Infrastructure.YouTube;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class YouTubeIngestionServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly YouTubeWorkspaceService _workspace;

    public YouTubeIngestionServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "YTIngestTests_" + Guid.NewGuid().ToString("N"));
        _workspace = new YouTubeWorkspaceService(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }
        catch { }
    }

    private class MockToolLocator : IYtDlpToolLocator
    {
        public Task<YouTubeToolStatus> LocateAsync(string? explicitPath = null, CancellationToken ct = default)
        {
            return Task.FromResult(YouTubeToolStatus.Available(@"C:\tools\yt-dlp.exe", "2024.08.06"));
        }
    }

    private class MockFfmpegLocator : IFfmpegToolLocator
    {
        public Task<FfmpegToolInfo> LocateToolsAsync(string? explicitFfmpegPath = null, string? explicitFfprobePath = null, CancellationToken ct = default)
        {
            return Task.FromResult(new FfmpegToolInfo(
                FfmpegPath: @"C:\tools\ffmpeg.exe",
                FfprobePath: @"C:\tools\ffprobe.exe",
                IsFfmpegAvailable: true,
                IsFfprobeAvailable: true,
                FfmpegVersionLine: "ffmpeg version 6.0",
                FfprobeVersionLine: "ffprobe version 6.0"
            ));
        }
    }

    private class CapturingProcessRunner : IYtDlpProcessRunner
    {
        public List<IReadOnlyList<string>> RecordedArguments { get; } = new();
        public bool KillCalled { get; private set; }
        private readonly Func<IReadOnlyList<string>, ProcessExecutionResult> _handler;

        public CapturingProcessRunner(Func<IReadOnlyList<string>, ProcessExecutionResult> handler)
        {
            _handler = handler;
        }

        public Task<ProcessExecutionResult> RunProcessAsync(string executablePath, IReadOnlyList<string> arguments, Action<string>? onStdOutLine = null, Action<string>? onStdErrLine = null, TimeSpan? timeout = null, CancellationToken ct = default)
        {
            RecordedArguments.Add(arguments);
            if (ct.IsCancellationRequested)
            {
                KillCalled = true;
                return Task.FromResult(ProcessExecutionResult.Cancelled());
            }
            return Task.FromResult(_handler(arguments));
        }
    }

    private class FakeAudioIngestionService : IAudioIngestionService
    {
        public bool WasCalled { get; private set; }
        public string? ReceivedSourcePath { get; private set; }
        private readonly bool _succeed;
        private readonly string _normalizedWavPath;

        public FakeAudioIngestionService(string normalizedWavPath, bool succeed = true)
        {
            _normalizedWavPath = normalizedWavPath;
            _succeed = succeed;
        }

        public Task<AudioIngestResult> IngestAudioAsync(AudioIngestRequest request, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            WasCalled = true;
            ReceivedSourcePath = request.FilePath;

            if (ct.IsCancellationRequested)
            {
                return Task.FromResult(AudioIngestResult.Failed(request.FilePath, "취소됨", "CANCELLED", request.JobId));
            }

            if (!_succeed)
            {
                return Task.FromResult(AudioIngestResult.Failed(request.FilePath, "Phase 7 normalization error", "PHASE_7_ERROR", request.JobId));
            }

            File.WriteAllBytes(_normalizedWavPath, new byte[] { 0x52, 0x49, 0x46, 0x46 });
            var meta = new AudioMetadata(request.FilePath, "wav", "pcm_s16le", 120, 44100, 2, 128000, 1024, 1);
            return Task.FromResult(AudioIngestResult.Successful(request.JobId, request.FilePath, _normalizedWavPath, meta));
        }

        public async Task<IReadOnlyList<AudioIngestResult>> IngestBatchAsync(IReadOnlyList<AudioIngestRequest> requests, IProgress<(int Current, int Total, string FileName, double Progress)>? progress = null, CancellationToken ct = default)
        {
            var results = new List<AudioIngestResult>();
            foreach (var req in requests)
            {
                results.Add(await IngestAudioAsync(req, ct: ct));
            }
            return results;
        }
    }

    [Fact]
    public async Task DownloadArguments_IncludeSafeFlags_And_ExcludeForbiddenFlags()
    {
        string validMetadata = @"
{
    ""id"": ""dQw4w9WgXcQ"",
    ""title"": ""Never Gonna Give You Up"",
    ""duration"": 213,
    ""channel"": ""RickAstleyVEVO""
}";

        var runner = new CapturingProcessRunner(args =>
        {
            if (args.Contains("--dump-single-json"))
            {
                return ProcessExecutionResult.Success(validMetadata);
            }
            // For download
            return ProcessExecutionResult.Failure(1, "stop-here-for-arg-check");
        });

        var service = new YouTubeIngestionService(
            toolLocator: new MockToolLocator(),
            processRunner: runner,
            ffmpegLocator: new MockFfmpegLocator(),
            workspaceService: _workspace
        );

        var req = new YouTubeIngestRequest("job_arg_check", "https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        await service.IngestYouTubeAsync(req);

        Assert.Equal(2, runner.RecordedArguments.Count);

        // Metadata probe args
        var probeArgs = runner.RecordedArguments[0];
        Assert.Contains("--ignore-config", probeArgs);
        Assert.Contains("--no-playlist", probeArgs);
        Assert.Contains("--dump-single-json", probeArgs);
        Assert.Contains("--skip-download", probeArgs);

        // Download args
        var downloadArgs = runner.RecordedArguments[1];
        Assert.Contains("--ignore-config", downloadArgs);
        Assert.Contains("--no-playlist", downloadArgs);
        Assert.Contains("-f", downloadArgs);
        Assert.Contains("bestaudio/best", downloadArgs);
        Assert.Contains("--extract-audio", downloadArgs);
        Assert.Contains("--audio-format", downloadArgs);
        Assert.Contains("wav", downloadArgs);
        Assert.Contains("--max-filesize", downloadArgs);
        Assert.Contains("500M", downloadArgs);
        Assert.Contains("--socket-timeout", downloadArgs);
        Assert.Contains("--retries", downloadArgs);
        Assert.Contains("--progress-template", downloadArgs);
        Assert.Contains("--ffmpeg-location", downloadArgs);

        // FORBIDDEN FLAGS CHECK
        Assert.DoesNotContain("--cookies", downloadArgs);
        Assert.DoesNotContain("--cookies-from-browser", downloadArgs);
        Assert.DoesNotContain("--exec", downloadArgs);
        Assert.DoesNotContain("--username", downloadArgs);
        Assert.DoesNotContain("--password", downloadArgs);
    }

    [Fact]
    public async Task ShellInjection_UrlPassedAsSingleArgument()
    {
        string urlWithSpecialChars = "https://www.youtube.com/watch?v=dQw4w9WgXcQ&si=123&test=\"foo\"'bar'";
        string validMetadata = @"
{
    ""id"": ""dQw4w9WgXcQ"",
    ""title"": ""Test Video"",
    ""duration"": 100
}";

        var runner = new CapturingProcessRunner(args => ProcessExecutionResult.Success(validMetadata));
        var service = new YouTubeIngestionService(
            toolLocator: new MockToolLocator(),
            processRunner: runner,
            ffmpegLocator: new MockFfmpegLocator(),
            workspaceService: _workspace
        );

        var req = new YouTubeIngestRequest("job_injection_check", urlWithSpecialChars);
        await service.IngestYouTubeAsync(req);

        // Check that CanonicalUrl is passed cleanly as a single argument element without shell quoting issues
        var probeArgs = runner.RecordedArguments[0];
        string urlArg = probeArgs.Last();
        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", urlArg);
    }

    [Fact]
    public async Task YouTubeIngest_ValidMetadata_DownloadsThenUsesPhase7()
    {
        string jobId = "job_happy_path";
        string validMetadata = @"
{
    ""id"": ""dQw4w9WgXcQ"",
    ""title"": ""Song Title"",
    ""duration"": 180,
    ""channel"": ""Artist Channel""
}";

        string normalizedWav = Path.Combine(_tempRoot, "normalized_phase7.wav");
        var fakeAudioService = new FakeAudioIngestionService(normalizedWav, succeed: true);

        var runner = new CapturingProcessRunner(args =>
        {
            if (args.Contains("--dump-single-json"))
            {
                return ProcessExecutionResult.Success(validMetadata);
            }

            // Simulate download output by writing source.wav
            string sourceWav = _workspace.GetSourceWavPath(jobId);
            File.WriteAllBytes(sourceWav, new byte[] { 1, 2, 3, 4 });
            return ProcessExecutionResult.Success("download completed");
        });

        var service = new YouTubeIngestionService(
            toolLocator: new MockToolLocator(),
            processRunner: runner,
            audioIngestionService: fakeAudioService,
            ffmpegLocator: new MockFfmpegLocator(),
            workspaceService: _workspace
        );

        var req = new YouTubeIngestRequest(jobId, "https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        var res = await service.IngestYouTubeAsync(req);

        Assert.True(res.Success);
        Assert.Equal("dQw4w9WgXcQ", res.VideoId);
        Assert.Equal("Song Title", res.Title);
        Assert.Equal("Artist Channel", res.ChannelName);
        Assert.Equal(180, res.DurationSeconds);
        Assert.Equal(normalizedWav, res.NormalizedAudioPath);

        // Phase 7 was invoked
        Assert.True(fakeAudioService.WasCalled);

        // YouTube temp source.wav was cleaned up
        string jobDir = Path.Combine(_tempRoot, jobId);
        Assert.False(Directory.Exists(jobDir), "Temporary YouTube job directory should be cleaned after handoff");

        // Phase 7 normalized.wav is preserved
        Assert.True(File.Exists(normalizedWav), "Phase 7 normalized.wav must be preserved");
    }

    [Fact]
    public async Task YouTubeIngest_MissingSourceWav_Fails()
    {
        string jobId = "job_missing_wav";
        string validMetadata = @"
{
    ""id"": ""dQw4w9WgXcQ"",
    ""title"": ""Song Title"",
    ""duration"": 180
}";

        var runner = new CapturingProcessRunner(args =>
        {
            if (args.Contains("--dump-single-json"))
            {
                return ProcessExecutionResult.Success(validMetadata);
            }
            // Exits 0 but does not create source.wav
            return ProcessExecutionResult.Success("done without file");
        });

        var service = new YouTubeIngestionService(
            toolLocator: new MockToolLocator(),
            processRunner: runner,
            ffmpegLocator: new MockFfmpegLocator(),
            workspaceService: _workspace
        );

        var req = new YouTubeIngestRequest(jobId, "https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        var res = await service.IngestYouTubeAsync(req);

        Assert.False(res.Success);
        Assert.Equal("OUTPUT_MISSING", res.ErrorCode);
    }

    [Fact]
    public async Task YouTubeIngest_Phase7Failure_CleansYouTubeWorkspace()
    {
        string jobId = "job_phase7_fail";
        string validMetadata = @"
{
    ""id"": ""dQw4w9WgXcQ"",
    ""title"": ""Song Title"",
    ""duration"": 180
}";

        string normalizedWav = Path.Combine(_tempRoot, "normalized_phase7.wav");
        var fakeAudioService = new FakeAudioIngestionService(normalizedWav, succeed: false);

        var runner = new CapturingProcessRunner(args =>
        {
            if (args.Contains("--dump-single-json"))
            {
                return ProcessExecutionResult.Success(validMetadata);
            }

            string sourceWav = _workspace.GetSourceWavPath(jobId);
            File.WriteAllBytes(sourceWav, new byte[] { 1, 2, 3, 4 });
            return ProcessExecutionResult.Success("done");
        });

        var service = new YouTubeIngestionService(
            toolLocator: new MockToolLocator(),
            processRunner: runner,
            audioIngestionService: fakeAudioService,
            ffmpegLocator: new MockFfmpegLocator(),
            workspaceService: _workspace
        );

        var req = new YouTubeIngestRequest(jobId, "https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        var res = await service.IngestYouTubeAsync(req);

        Assert.False(res.Success);
        Assert.Equal("AUDIO_INGEST_ERROR", res.ErrorCode);

        string jobDir = Path.Combine(_tempRoot, jobId);
        Assert.False(Directory.Exists(jobDir), "YouTube job directory should be cleaned on Phase 7 failure");
    }

    [Fact]
    public async Task YouTubeIngest_CancelDuringDownload_KillsProcess_And_CleansWorkspace()
    {
        string jobId = "job_cancel_dl";
        string validMetadata = @"
{
    ""id"": ""dQw4w9WgXcQ"",
    ""title"": ""Song Title"",
    ""duration"": 180
}";

        using var cts = new CancellationTokenSource();

        var runner = new CapturingProcessRunner(args =>
        {
            if (args.Contains("--dump-single-json"))
            {
                return ProcessExecutionResult.Success(validMetadata);
            }

            // Cancel during download
            cts.Cancel();
            return ProcessExecutionResult.Cancelled();
        });

        var service = new YouTubeIngestionService(
            toolLocator: new MockToolLocator(),
            processRunner: runner,
            ffmpegLocator: new MockFfmpegLocator(),
            workspaceService: _workspace
        );

        var req = new YouTubeIngestRequest(jobId, "https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        var res = await service.IngestYouTubeAsync(req, ct: cts.Token);

        Assert.False(res.Success);
        Assert.Equal("CANCELLED", res.ErrorCode);

        string jobDir = Path.Combine(_tempRoot, jobId);
        Assert.False(Directory.Exists(jobDir), "YouTube job workspace should be cleaned on cancel");
    }

    [Fact]
    public async Task YouTubeIngest_OneFailure_DoesNotDeletePreviousCompletedResult()
    {
        string firstNormalizedWav = Path.Combine(_tempRoot, "normalized_first.wav");
        File.WriteAllBytes(firstNormalizedWav, new byte[] { 10, 20, 30 });

        string secondJobId = "job_second_fail";
        var runner = new CapturingProcessRunner(_ => ProcessExecutionResult.Failure(1, "network error"));

        var service = new YouTubeIngestionService(
            toolLocator: new MockToolLocator(),
            processRunner: runner,
            ffmpegLocator: new MockFfmpegLocator(),
            workspaceService: _workspace
        );

        var req2 = new YouTubeIngestRequest(secondJobId, "https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        var res2 = await service.IngestYouTubeAsync(req2);

        Assert.False(res2.Success);
        Assert.True(File.Exists(firstNormalizedWav), "Previous completed normalized WAV must not be touched by subsequent failure");
    }
}
