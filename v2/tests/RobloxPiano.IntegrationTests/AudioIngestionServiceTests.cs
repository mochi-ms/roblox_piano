using RobloxPiano.Core.Audio;
using RobloxPiano.Infrastructure.Audio;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class AudioIngestionServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _workspaceDir;
    private readonly string _sourceDir;
    private readonly string _fakeFfmpeg;
    private readonly string _fakeFfprobe;

    public AudioIngestionServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"rp_audio_svc_test_{Guid.NewGuid():N}");
        _workspaceDir = Path.Combine(_tempDir, "Workspace");
        _sourceDir = Path.Combine(_tempDir, "Sources");

        Directory.CreateDirectory(_workspaceDir);
        Directory.CreateDirectory(_sourceDir);

        _fakeFfmpeg = Path.Combine(_tempDir, "ffmpeg.exe");
        _fakeFfprobe = Path.Combine(_tempDir, "ffprobe.exe");
        File.WriteAllText(_fakeFfmpeg, "fake ffmpeg");
        File.WriteAllText(_fakeFfprobe, "fake ffprobe");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    private string CreateFakeAudioFile(string filename, string content = "RIFF....WAVEfmt ")
    {
        string p = Path.Combine(_sourceDir, filename);
        File.WriteAllText(p, content);
        return p;
    }

    [Fact]
    public async Task AudioIngest_ValidMp3_NormalizesToCanonicalWav()
    {
        string sourceMp3 = CreateFakeAudioFile("recital.mp3");

        var fakeRunner = new MockAudioProcessRunner(
            sourceProbeJson: @"{
                ""streams"": [{ ""codec_type"": ""audio"", ""codec_name"": ""mp3"", ""channels"": 2, ""sample_rate"": ""44100"" }],
                ""format"": { ""format_name"": ""mp3"", ""duration"": ""120.0"", ""size"": ""5000000"" }
            }",
            outputProbeJson: @"{
                ""streams"": [{ ""codec_type"": ""audio"", ""codec_name"": ""pcm_s16le"", ""channels"": 1, ""sample_rate"": ""22050"" }],
                ""format"": { ""format_name"": ""wav"", ""duration"": ""120.0"", ""size"": ""5292000"" }
            }"
        );

        var toolLocator = new MockToolLocator(_fakeFfmpeg, _fakeFfprobe);
        var metadataReader = new FfprobeMetadataReader(fakeRunner);
        var workspaceService = new AudioWorkspaceService(_workspaceDir);
        var service = new AudioIngestionService(toolLocator, fakeRunner, metadataReader, workspaceService);

        var req = new AudioIngestRequest(sourceMp3, "job_123");
        var result = await service.IngestAudioAsync(req);

        Assert.True(result.Success);
        Assert.Equal("job_123", result.JobId);
        Assert.NotNull(result.NormalizedAudioPath);
        Assert.True(File.Exists(result.NormalizedAudioPath));
        Assert.EndsWith("normalized.wav", result.NormalizedAudioPath);
        Assert.True(File.Exists(sourceMp3)); // Source untouched
    }

    [Fact]
    public async Task AudioIngest_ValidFlac_NormalizesToCanonicalWav()
    {
        string sourceFlac = CreateFakeAudioFile("song.flac");

        var fakeRunner = new MockAudioProcessRunner(
            sourceProbeJson: @"{
                ""streams"": [{ ""codec_type"": ""audio"", ""codec_name"": ""flac"", ""channels"": 2, ""sample_rate"": ""96000"" }],
                ""format"": { ""format_name"": ""flac"", ""duration"": ""60.0"", ""size"": ""10000000"" }
            }",
            outputProbeJson: @"{
                ""streams"": [{ ""codec_type"": ""audio"", ""codec_name"": ""pcm_s16le"", ""channels"": 1, ""sample_rate"": ""22050"" }],
                ""format"": { ""format_name"": ""wav"", ""duration"": ""60.0"", ""size"": ""2646000"" }
            }"
        );

        var toolLocator = new MockToolLocator(_fakeFfmpeg, _fakeFfprobe);
        var metadataReader = new FfprobeMetadataReader(fakeRunner);
        var workspaceService = new AudioWorkspaceService(_workspaceDir);
        var service = new AudioIngestionService(toolLocator, fakeRunner, metadataReader, workspaceService);

        var req = new AudioIngestRequest(sourceFlac, "job_flac");
        var result = await service.IngestAudioAsync(req);

        Assert.True(result.Success);
        Assert.Equal("job_flac", result.JobId);
        Assert.True(File.Exists(result.NormalizedAudioPath));
    }

    [Fact]
    public async Task AudioIngest_Success_RetainsNormalizedArtifact()
    {
        string sourceWav = CreateFakeAudioFile("piano.wav");

        var fakeRunner = new MockAudioProcessRunner(
            sourceProbeJson: @"{
                ""streams"": [{ ""codec_type"": ""audio"", ""codec_name"": ""pcm_s16le"", ""channels"": 2, ""sample_rate"": ""44100"" }],
                ""format"": { ""format_name"": ""wav"", ""duration"": ""45.0"", ""size"": ""3000000"" }
            }",
            outputProbeJson: @"{
                ""streams"": [{ ""codec_type"": ""audio"", ""codec_name"": ""pcm_s16le"", ""channels"": 1, ""sample_rate"": ""22050"" }],
                ""format"": { ""format_name"": ""wav"", ""duration"": ""45.0"", ""size"": ""1984500"" }
            }"
        );

        var toolLocator = new MockToolLocator(_fakeFfmpeg, _fakeFfprobe);
        var metadataReader = new FfprobeMetadataReader(fakeRunner);
        var workspaceService = new AudioWorkspaceService(_workspaceDir);
        var service = new AudioIngestionService(toolLocator, fakeRunner, metadataReader, workspaceService);

        var req = new AudioIngestRequest(sourceWav, "job_success_retention");
        var result = await service.IngestAudioAsync(req);

        Assert.True(result.Success);
        string finalWav = Path.Combine(_workspaceDir, "job_success_retention", "normalized.wav");
        Assert.True(File.Exists(finalWav));
        Assert.True(File.Exists(sourceWav)); // Original source untouched
    }

    [Fact]
    public async Task AudioIngest_InvalidJobId_FailsFast()
    {
        string sourceWav = CreateFakeAudioFile("test.wav");
        var service = new AudioIngestionService();

        var req = new AudioIngestRequest(sourceWav, "../../outside_job");
        var result = await service.IngestAudioAsync(req);

        Assert.False(result.Success);
        Assert.Equal("INVALID_JOB_ID", result.ErrorCode);
    }

    [Fact]
    public async Task AudioIngest_ConversionFailure_CleansTempOutput()
    {
        string sourceWav = CreateFakeAudioFile("bad.wav");

        var fakeRunner = new MockAudioProcessRunner(
            sourceProbeJson: @"{
                ""streams"": [{ ""codec_type"": ""audio"", ""codec_name"": ""pcm_s16le"", ""channels"": 2, ""sample_rate"": ""44100"" }],
                ""format"": { ""format_name"": ""wav"", ""duration"": ""30.0"", ""size"": ""1000000"" }
            }",
            conversionFails: true
        );

        var toolLocator = new MockToolLocator(_fakeFfmpeg, _fakeFfprobe);
        var metadataReader = new FfprobeMetadataReader(fakeRunner);
        var workspaceService = new AudioWorkspaceService(_workspaceDir);
        var service = new AudioIngestionService(toolLocator, fakeRunner, metadataReader, workspaceService);

        var req = new AudioIngestRequest(sourceWav, "job_fail");
        var result = await service.IngestAudioAsync(req);

        Assert.False(result.Success);
        Assert.Contains(AudioError.ConversionFailed, result.ErrorMessage);
        Assert.False(Directory.Exists(Path.Combine(_workspaceDir, "job_fail")));
        Assert.True(File.Exists(sourceWav));
    }

    [Fact]
    public async Task AudioIngest_OutputProbeFailure_CleansInvalidOutput()
    {
        string sourceWav = CreateFakeAudioFile("stereo_fail.wav");

        var fakeRunner = new MockAudioProcessRunner(
            sourceProbeJson: @"{
                ""streams"": [{ ""codec_type"": ""audio"", ""codec_name"": ""pcm_s16le"", ""channels"": 2, ""sample_rate"": ""44100"" }],
                ""format"": { ""format_name"": ""wav"", ""duration"": ""30.0"", ""size"": ""1000000"" }
            }",
            // Output probe returns 2 channels instead of 1 (canonical mono violation)
            outputProbeJson: @"{
                ""streams"": [{ ""codec_type"": ""audio"", ""codec_name"": ""pcm_s16le"", ""channels"": 2, ""sample_rate"": ""22050"" }],
                ""format"": { ""format_name"": ""wav"", ""duration"": ""30.0"", ""size"": ""1000000"" }
            }"
        );

        var toolLocator = new MockToolLocator(_fakeFfmpeg, _fakeFfprobe);
        var metadataReader = new FfprobeMetadataReader(fakeRunner);
        var workspaceService = new AudioWorkspaceService(_workspaceDir);
        var service = new AudioIngestionService(toolLocator, fakeRunner, metadataReader, workspaceService);

        var req = new AudioIngestRequest(sourceWav, "job_invalid_out");
        var result = await service.IngestAudioAsync(req);

        Assert.False(result.Success);
        Assert.Equal(AudioError.OutputValidationFailed, result.ErrorMessage);
        Assert.False(Directory.Exists(Path.Combine(_workspaceDir, "job_invalid_out")));
        Assert.True(File.Exists(sourceWav));
    }

    [Fact]
    public async Task AudioIngest_RunnerReturnsCancelled_CleansWorkspaceAndSurfacesCancellation()
    {
        string sourceWav = CreateFakeAudioFile("cancel_runner.wav");

        var fakeRunner = new MockAudioProcessRunner(
            sourceProbeJson: @"{
                ""streams"": [{ ""codec_type"": ""audio"", ""codec_name"": ""pcm_s16le"", ""channels"": 1, ""sample_rate"": ""44100"" }],
                ""format"": { ""format_name"": ""wav"", ""duration"": ""30.0"", ""size"": ""1000000"" }
            }",
            cancelDuringConversion: true
        );

        var toolLocator = new MockToolLocator(_fakeFfmpeg, _fakeFfprobe);
        var metadataReader = new FfprobeMetadataReader(fakeRunner);
        var workspaceService = new AudioWorkspaceService(_workspaceDir);
        var service = new AudioIngestionService(toolLocator, fakeRunner, metadataReader, workspaceService);

        var req = new AudioIngestRequest(sourceWav, "job_cancel_runner");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await service.IngestAudioAsync(req);
        });

        Assert.False(Directory.Exists(Path.Combine(_workspaceDir, "job_cancel_runner")));
        Assert.True(File.Exists(sourceWav)); // Source untouched
    }

    [Fact]
    public async Task AudioIngest_CancellationAfterConversion_CleansTempOutput()
    {
        string sourceWav = CreateFakeAudioFile("cancel_after_conv.wav");

        using var cts = new CancellationTokenSource();

        var fakeRunner = new MockAudioProcessRunner(
            sourceProbeJson: @"{
                ""streams"": [{ ""codec_type"": ""audio"", ""codec_name"": ""pcm_s16le"", ""channels"": 1, ""sample_rate"": ""44100"" }],
                ""format"": { ""format_name"": ""wav"", ""duration"": ""30.0"", ""size"": ""1000000"" }
            }",
            onConversionSuccess: () =>
            {
                // Trigger cancellation right after conversion completes
                cts.Cancel();
            }
        );

        var toolLocator = new MockToolLocator(_fakeFfmpeg, _fakeFfprobe);
        var metadataReader = new FfprobeMetadataReader(fakeRunner);
        var workspaceService = new AudioWorkspaceService(_workspaceDir);
        var service = new AudioIngestionService(toolLocator, fakeRunner, metadataReader, workspaceService);

        var req = new AudioIngestRequest(sourceWav, "job_cancel_after_conv");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await service.IngestAudioAsync(req, ct: cts.Token);
        });

        Assert.False(Directory.Exists(Path.Combine(_workspaceDir, "job_cancel_after_conv")));
        Assert.True(File.Exists(sourceWav));
    }

    [Fact]
    public async Task AudioIngest_CancellationAfterCommit_CleansNormalizedArtifact()
    {
        string sourceWav = CreateFakeAudioFile("cancel_after_commit.wav");

        using var cts = new CancellationTokenSource();

        var fakeRunner = new MockAudioProcessRunner(
            sourceProbeJson: @"{
                ""streams"": [{ ""codec_type"": ""audio"", ""codec_name"": ""pcm_s16le"", ""channels"": 1, ""sample_rate"": ""44100"" }],
                ""format"": { ""format_name"": ""wav"", ""duration"": ""30.0"", ""size"": ""1000000"" }
            }",
            outputProbeJson: @"{
                ""streams"": [{ ""codec_type"": ""audio"", ""codec_name"": ""pcm_s16le"", ""channels"": 1, ""sample_rate"": ""22050"" }],
                ""format"": { ""format_name"": ""wav"", ""duration"": ""30.0"", ""size"": ""1000000"" }
            }",
            cancelDuringOutputProbe: true
        );

        var toolLocator = new MockToolLocator(_fakeFfmpeg, _fakeFfprobe);
        var metadataReader = new FfprobeMetadataReader(fakeRunner);
        var workspaceService = new AudioWorkspaceService(_workspaceDir);
        var service = new AudioIngestionService(toolLocator, fakeRunner, metadataReader, workspaceService);

        var req = new AudioIngestRequest(sourceWav, "job_cancel_after_commit");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await service.IngestAudioAsync(req, ct: cts.Token);
        });

        Assert.False(Directory.Exists(Path.Combine(_workspaceDir, "job_cancel_after_commit")));
        Assert.True(File.Exists(sourceWav));
    }

    [Fact]
    public async Task AudioBatch_AllValid_AllSucceed()
    {
        string f1 = CreateFakeAudioFile("track1.mp3");
        string f2 = CreateFakeAudioFile("track2.wav");

        var fakeRunner = new MockAudioProcessRunner(
            sourceProbeJson: @"{
                ""streams"": [{ ""codec_type"": ""audio"", ""codec_name"": ""mp3"", ""channels"": 2, ""sample_rate"": ""44100"" }],
                ""format"": { ""format_name"": ""mp3"", ""duration"": ""30.0"", ""size"": ""1000000"" }
            }",
            outputProbeJson: @"{
                ""streams"": [{ ""codec_type"": ""audio"", ""codec_name"": ""pcm_s16le"", ""channels"": 1, ""sample_rate"": ""22050"" }],
                ""format"": { ""format_name"": ""wav"", ""duration"": ""30.0"", ""size"": ""1000000"" }
            }"
        );

        var toolLocator = new MockToolLocator(_fakeFfmpeg, _fakeFfprobe);
        var metadataReader = new FfprobeMetadataReader(fakeRunner);
        var workspaceService = new AudioWorkspaceService(_workspaceDir);
        var service = new AudioIngestionService(toolLocator, fakeRunner, metadataReader, workspaceService);

        var requests = new[]
        {
            new AudioIngestRequest(f1, "b1"),
            new AudioIngestRequest(f2, "b2")
        };

        var results = await service.IngestBatchAsync(requests);

        Assert.Equal(2, results.Count);
        Assert.True(results[0].Success);
        Assert.True(results[1].Success);
    }

    [Fact]
    public async Task AudioBatch_OneFailure_OthersContinue()
    {
        string f1 = CreateFakeAudioFile("valid.mp3");
        string f2 = Path.Combine(_sourceDir, "missing.mp3"); // Missing file
        string f3 = CreateFakeAudioFile("valid2.wav");

        var fakeRunner = new MockAudioProcessRunner(
            sourceProbeJson: @"{
                ""streams"": [{ ""codec_type"": ""audio"", ""codec_name"": ""mp3"", ""channels"": 2, ""sample_rate"": ""44100"" }],
                ""format"": { ""format_name"": ""mp3"", ""duration"": ""30.0"", ""size"": ""1000000"" }
            }",
            outputProbeJson: @"{
                ""streams"": [{ ""codec_type"": ""audio"", ""codec_name"": ""pcm_s16le"", ""channels"": 1, ""sample_rate"": ""22050"" }],
                ""format"": { ""format_name"": ""wav"", ""duration"": ""30.0"", ""size"": ""1000000"" }
            }"
        );

        var toolLocator = new MockToolLocator(_fakeFfmpeg, _fakeFfprobe);
        var metadataReader = new FfprobeMetadataReader(fakeRunner);
        var workspaceService = new AudioWorkspaceService(_workspaceDir);
        var service = new AudioIngestionService(toolLocator, fakeRunner, metadataReader, workspaceService);

        var requests = new[]
        {
            new AudioIngestRequest(f1, "b1"),
            new AudioIngestRequest(f2, "b2"),
            new AudioIngestRequest(f3, "b3")
        };

        var results = await service.IngestBatchAsync(requests);

        Assert.Equal(3, results.Count);
        Assert.True(results[0].Success);
        Assert.False(results[1].Success);
        Assert.Equal(AudioError.FileNotFound, results[1].ErrorMessage);
        Assert.True(results[2].Success);
    }

    private class MockToolLocator : IFfmpegToolLocator
    {
        private readonly FfmpegToolInfo _info;
        public MockToolLocator(string ffmpeg, string ffprobe) =>
            _info = new FfmpegToolInfo(ffmpeg, ffprobe, true, true, "ffmpeg 6.0", "ffprobe 6.0");

        public Task<FfmpegToolInfo> LocateToolsAsync(string? explicitFfmpegPath = null, string? explicitFfprobePath = null, CancellationToken ct = default) =>
            Task.FromResult(_info);
    }

    private class MockAudioProcessRunner : IFfmpegProcessRunner
    {
        private readonly string _sourceProbeJson;
        private readonly string? _outputProbeJson;
        private readonly bool _conversionFails;
        private readonly bool _cancelDuringConversion;
        private readonly bool _cancelDuringOutputProbe;
        private readonly Action? _onConversionSuccess;

        public MockAudioProcessRunner(
            string sourceProbeJson,
            string? outputProbeJson = null,
            bool conversionFails = false,
            bool cancelDuringConversion = false,
            bool cancelDuringOutputProbe = false,
            Action? onConversionSuccess = null)
        {
            _sourceProbeJson = sourceProbeJson;
            _outputProbeJson = outputProbeJson ?? sourceProbeJson;
            _conversionFails = conversionFails;
            _cancelDuringConversion = cancelDuringConversion;
            _cancelDuringOutputProbe = cancelDuringOutputProbe;
            _onConversionSuccess = onConversionSuccess;
        }

        public Task<ProcessExecutionResult> RunProcessAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            Action<string>? onStdOutLine = null,
            Action<string>? onStdErrLine = null,
            TimeSpan? timeout = null,
            CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested)
            {
                return Task.FromResult(ProcessExecutionResult.Cancelled());
            }

            // If it's ffprobe
            if (arguments.Contains("-print_format"))
            {
                string target = arguments[^1];
                if (target.EndsWith("normalized.wav") && _outputProbeJson != null)
                {
                    if (_cancelDuringOutputProbe)
                    {
                        return Task.FromResult(ProcessExecutionResult.Cancelled());
                    }
                    return Task.FromResult(ProcessExecutionResult.Success(_outputProbeJson));
                }
                return Task.FromResult(ProcessExecutionResult.Success(_sourceProbeJson));
            }

            // If it's ffmpeg conversion
            if (_cancelDuringConversion)
            {
                return Task.FromResult(ProcessExecutionResult.Cancelled());
            }

            if (_conversionFails)
            {
                return Task.FromResult(ProcessExecutionResult.Failure(1, "Simulated ffmpeg conversion error"));
            }

            // Create target file on disk so validation passes
            string outputPath = arguments[^1];
            string? dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(outputPath, "RIFF_WAV_SIMULATED_CONTENT");

            onStdOutLine?.Invoke("out_time_us=30000000");
            onStdOutLine?.Invoke("progress=end");

            _onConversionSuccess?.Invoke();

            return Task.FromResult(ProcessExecutionResult.Success(string.Empty));
        }
    }
}
