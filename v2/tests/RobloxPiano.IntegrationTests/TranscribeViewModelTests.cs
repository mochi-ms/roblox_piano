using RobloxPiano.Core.Audio;
using RobloxPiano.Desktop.ViewModels;
using RobloxPiano.Infrastructure.Audio;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class TranscribeViewModelTests : IDisposable
{
    private readonly string _tempDir;

    public TranscribeViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"rp_transcribe_vm_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
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

    private string CreateFakeAudio(string filename)
    {
        string p = Path.Combine(_tempDir, filename);
        File.WriteAllText(p, "fake audio");
        return p;
    }

    [Fact]
    public void AudioViewModel_AddFiles_ShowsQueue()
    {
        var vm = new TranscribeViewModel();
        var f1 = CreateFakeAudio("song1.mp3");
        var f2 = CreateFakeAudio("song2.wav");

        vm.AddFiles(new[] { f1, f2 });

        Assert.True(vm.HasItems);
        Assert.Equal(2, vm.QueueItems.Count);
        Assert.Equal("song1.mp3", vm.QueueItems[0].FileName);
        Assert.Equal("song2.wav", vm.QueueItems[1].FileName);
        Assert.Equal(AudioItemStatus.Pending, vm.QueueItems[0].Status);
    }

    [Fact]
    public async Task AudioViewModel_SuccessShowsPreparedState()
    {
        var f1 = CreateFakeAudio("recital.mp3");
        var fakeService = new MockAudioService(
            onIngest: req => AudioIngestResult.Successful(
                req.JobId,
                req.FilePath,
                Path.Combine(_tempDir, "normalized.wav"),
                new AudioMetadata(req.FilePath, "mp3", "mp3", 120.0, 44100, 2, 320000, 1000000, 1)
            )
        );

        var vm = new TranscribeViewModel(fakeService);
        vm.AddFiles(new[] { f1 });

        await vm.StartIngestAsync();

        Assert.Single(vm.QueueItems);
        var item = vm.QueueItems[0];
        Assert.Equal(AudioItemStatus.Prepared, item.Status);
        Assert.Equal("준비 완료", item.StatusText);
        Assert.True(item.IsPrepared);
        Assert.Equal("02:00", item.DurationText);
        Assert.Contains("준비 완료", vm.ProgressStatusText);
    }

    [Fact]
    public async Task AudioViewModel_Start_RemainsResponsive()
    {
        var f1 = CreateFakeAudio("slow.wav");
        var gate = new ManualResetEventSlim(false);
        var enteredSignal = new TaskCompletionSource<bool>();

        var fakeService = new MockAudioService(
            onIngest: req =>
            {
                enteredSignal.TrySetResult(true);
                gate.Wait(5000);
                return AudioIngestResult.Successful(
                    req.JobId,
                    req.FilePath,
                    Path.Combine(_tempDir, "normalized.wav"),
                    new AudioMetadata(req.FilePath, "wav", "pcm_s16le", 60.0, 22050, 1, 352800, 1000000, 1)
                );
            }
        );

        var vm = new TranscribeViewModel(fakeService);
        vm.AddFiles(new[] { f1 });

        var ingestTask = vm.StartIngestAsync();

        // Wait for worker thread to enter fake service
        await enteredSignal.Task;

        // Calling thread must not be blocked
        Assert.False(ingestTask.IsCompleted);
        Assert.True(vm.IsProcessing);

        gate.Set();
        await ingestTask;

        Assert.True(ingestTask.IsCompletedSuccessfully);
        Assert.False(vm.IsProcessing);
    }

    [Fact]
    public async Task AudioViewModel_CancelCurrentAndRemaining()
    {
        var f1 = CreateFakeAudio("song1.mp3");
        var f2 = CreateFakeAudio("song2.mp3");

        var startedSignal = new TaskCompletionSource<bool>();
        var fakeService = new MockAudioService(
            onIngest: req =>
            {
                startedSignal.TrySetResult(true);
                throw new OperationCanceledException();
            }
        );

        var vm = new TranscribeViewModel(fakeService);
        vm.AddFiles(new[] { f1, f2 });

        var ingestTask = vm.StartIngestAsync();

        await startedSignal.Task;
        vm.CancelIngest();

        await ingestTask;

        Assert.Equal(AudioItemStatus.Cancelled, vm.QueueItems[0].Status);
        Assert.Equal(AudioItemStatus.Cancelled, vm.QueueItems[1].Status);
        Assert.False(vm.IsProcessing);
        Assert.Equal("취소됨", vm.ProgressStatusText);
    }

    [Fact]
    public async Task AudioViewModel_MissingFfmpeg_ShowsFriendlyStatus()
    {
        var mockLocator = new MockFfmpegLocator(isAvailable: false);
        var vm = new TranscribeViewModel(toolLocator: mockLocator);

        await vm.CheckToolsAsync();

        Assert.False(vm.IsFfmpegReady);
        Assert.Contains("찾을 수 없습니다", vm.FfmpegStatusText);
    }

    private class MockAudioService : IAudioIngestionService
    {
        private readonly Func<AudioIngestRequest, AudioIngestResult> _onIngest;

        public MockAudioService(Func<AudioIngestRequest, AudioIngestResult> onIngest)
        {
            _onIngest = onIngest;
        }

        public Task<AudioIngestResult> IngestAudioAsync(AudioIngestRequest request, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException();
            }

            var result = _onIngest(request);
            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<AudioIngestResult>> IngestBatchAsync(IReadOnlyList<AudioIngestRequest> requests, IProgress<(int Current, int Total, string FileName, double Progress)>? progress = null, CancellationToken ct = default)
        {
            var results = requests.Select(_onIngest).ToList();
            return Task.FromResult<IReadOnlyList<AudioIngestResult>>(results);
        }
    }

    private class MockFfmpegLocator : IFfmpegToolLocator
    {
        private readonly bool _isAvailable;

        public MockFfmpegLocator(bool isAvailable)
        {
            _isAvailable = isAvailable;
        }

        public Task<FfmpegToolInfo> LocateToolsAsync(string? explicitFfmpegPath = null, string? explicitFfprobePath = null, CancellationToken ct = default)
        {
            return Task.FromResult(new FfmpegToolInfo(
                _isAvailable ? @"C:\tools\ffmpeg.exe" : null,
                _isAvailable ? @"C:\tools\ffprobe.exe" : null,
                _isAvailable,
                _isAvailable,
                _isAvailable ? "ffmpeg 6.0" : null,
                _isAvailable ? "ffprobe 6.0" : null
            ));
        }
    }
}
