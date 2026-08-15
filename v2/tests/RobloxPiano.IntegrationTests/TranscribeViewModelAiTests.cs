using System.IO;
using RobloxPiano.Core.Audio;
using RobloxPiano.Core.Importing;
using RobloxPiano.Core.Music;
using RobloxPiano.Core.Transcription;
using RobloxPiano.Desktop.ViewModels;
using RobloxPiano.Infrastructure.Audio;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class TranscribeViewModelAiTests : IDisposable
{
    private readonly string _tempRoot;

    public TranscribeViewModelAiTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "rp_vm_ai_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch { }
    }

    private class MockTranscriptionEngine : ITranscriptionEngine
    {
        private readonly bool _shouldSucceed;
        private readonly bool _shouldThrow;
        private readonly TimeSpan _delay;

        public MockTranscriptionEngine(bool shouldSucceed = true, bool shouldThrow = false, TimeSpan delay = default)
        {
            _shouldSucceed = shouldSucceed;
            _shouldThrow = shouldThrow;
            _delay = delay;
        }

        public Task<TranscriptionEngineStatus> CheckAvailabilityAsync(CancellationToken ct = default)
        {
            return Task.FromResult(TranscriptionEngineStatus.Available(@"C:\mock\python.exe", "3.11.2", "0.4.0"));
        }

        public async Task<TranscriptionResult> TranscribeAsync(TranscriptionRequest request, IProgress<TranscriptionProgress>? progress = null, CancellationToken ct = default)
        {
            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, ct);
            }

            if (_shouldThrow)
            {
                throw new InvalidOperationException("Simulated engine crash");
            }

            if (_shouldSucceed)
            {
                var timeline = new MusicTimeline("AI Score");
                timeline.AddNote(new(60, 0.0, 1.0, 80));
                timeline.AddNote(new(64, 1.0, 2.0, 80));
                return TranscriptionResult.Successful(
                    request.JobId,
                    request.NormalizedAudioPath,
                    Path.Combine(Path.GetDirectoryName(request.NormalizedAudioPath)!, "transcription.mid"),
                    timeline,
                    playableNoteCount: 2,
                    outOfRangeNoteCount: 0,
                    minPitch: 60,
                    maxPitch: 64,
                    runtimeSeconds: 0.5
                );
            }

            return TranscriptionResult.Failed(request.JobId, request.NormalizedAudioPath, "Inference error", "ERR_INFERENCE");
        }

        public void Dispose() { }
    }

    [Fact]
    public async Task StartAiTranscriptionAsync_Success_UpdatesItemToAiCompleted()
    {
        string sampleAudio = Path.Combine(_tempRoot, "sample.mp3");
        File.WriteAllBytes(sampleAudio, new byte[] { 1, 2, 3 });

        var engine = new MockTranscriptionEngine(shouldSucceed: true);
        using var vm = new TranscribeViewModel(transcriptionEngine: engine);

        vm.AddFiles(new[] { sampleAudio });
        var item = vm.QueueItems[0];

        // Mark prepared
        string normWav = Path.Combine(_tempRoot, "normalized.wav");
        File.WriteAllBytes(normWav, new byte[] { 1, 2, 3 });
        item.SetPrepared(AudioIngestResult.Successful(item.JobId, sampleAudio, normWav, new AudioMetadata(sampleAudio, "wav", "pcm_s16le", 2.0, 22050, 1, 352800, 88244, 1)));

        await vm.StartAiTranscriptionAsync(item);

        Assert.Equal(AudioItemStatus.AiCompleted, item.Status);
        Assert.True(item.IsAiCompleted);
        Assert.False(item.IsAiProcessing);
        Assert.NotNull(item.AiResult);
        Assert.Equal(2, item.AiResult.NoteCount);
        Assert.Contains("2음", item.NoteStatsText);
    }

    [Fact]
    public async Task StartAiTranscriptionAsync_Failure_UpdatesItemToFailed()
    {
        string sampleAudio = Path.Combine(_tempRoot, "sample.mp3");
        File.WriteAllBytes(sampleAudio, new byte[] { 1, 2, 3 });

        var engine = new MockTranscriptionEngine(shouldSucceed: false);
        using var vm = new TranscribeViewModel(transcriptionEngine: engine);

        vm.AddFiles(new[] { sampleAudio });
        var item = vm.QueueItems[0];

        string normWav = Path.Combine(_tempRoot, "normalized.wav");
        File.WriteAllBytes(normWav, new byte[] { 1, 2, 3 });
        item.SetPrepared(AudioIngestResult.Successful(item.JobId, sampleAudio, normWav, new AudioMetadata(sampleAudio, "wav", "pcm_s16le", 2.0, 22050, 1, 352800, 88244, 1)));

        await vm.StartAiTranscriptionAsync(item);

        Assert.Equal(AudioItemStatus.Failed, item.Status);
        Assert.True(item.IsFailed);
        Assert.False(item.IsAiProcessing);
        Assert.Contains("Inference error", item.ErrorMessage);
    }

    [Fact]
    public async Task StartBatchAiTranscriptionAsync_ProcessesAllPreparedItems()
    {
        var engine = new MockTranscriptionEngine(shouldSucceed: true);
        using var vm = new TranscribeViewModel(transcriptionEngine: engine);

        for (int i = 0; i < 3; i++)
        {
            string p = Path.Combine(_tempRoot, $"track_{i}.mp3");
            File.WriteAllBytes(p, new byte[] { 1, 2, 3 });
            vm.AddFiles(new[] { p });

            string norm = Path.Combine(_tempRoot, $"norm_{i}.wav");
            File.WriteAllBytes(norm, new byte[] { 1, 2, 3 });
            vm.QueueItems[i].SetPrepared(AudioIngestResult.Successful(vm.QueueItems[i].JobId, p, norm, new AudioMetadata(p, "wav", "pcm_s16le", 2.0, 22050, 1, 352800, 88244, 1)));
        }

        await vm.StartBatchAiTranscriptionAsync();

        Assert.All(vm.QueueItems, it => Assert.Equal(AudioItemStatus.AiCompleted, it.Status));
    }

    [Fact]
    public void OpenInPlayer_RaisesOpenScoreRequestedWithTimeline()
    {
        var engine = new MockTranscriptionEngine(shouldSucceed: true);
        using var vm = new TranscribeViewModel(transcriptionEngine: engine);

        string sampleAudio = Path.Combine(_tempRoot, "sample.mp3");
        File.WriteAllBytes(sampleAudio, new byte[] { 1, 2, 3 });
        vm.AddFiles(new[] { sampleAudio });

        var item = vm.QueueItems[0];
        var timeline = new MusicTimeline("Test Score");
        timeline.AddNote(new(60, 0.0, 1.0, 80));
        item.SetAiCompleted(TranscriptionResult.Successful(item.JobId, sampleAudio, "test.mid", timeline, 1, 0, 60, 60));

        MusicTimeline? receivedTimeline = null;
        vm.OpenScoreRequested += (_, tl) => receivedTimeline = tl;

        vm.OpenInPlayer(item);

        Assert.NotNull(receivedTimeline);
        Assert.Equal("Test Score", receivedTimeline.Title);
    }
}
