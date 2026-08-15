using RobloxPiano.Core.Audio;
using RobloxPiano.Core.Importing;
using RobloxPiano.Core.Music;
using RobloxPiano.Core.Piano;
using RobloxPiano.Core.Transcription;
using RobloxPiano.Core.YouTube;
using RobloxPiano.Desktop.ViewModels;
using RobloxPiano.Infrastructure.Audio;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class TranscribeViewModelYouTubeTests : IDisposable
{
    private readonly string _tempRoot;

    public TranscribeViewModelYouTubeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "TranscribeVMYTTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
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

    private class MockYouTubeService : IYouTubeIngestionService
    {
        public bool CheckToolStatusCalled { get; private set; }
        public bool IngestCalled { get; private set; }
        private readonly bool _succeed;
        private readonly string _normalizedWav;

        public MockYouTubeService(string normalizedWav, bool succeed = true)
        {
            _normalizedWav = normalizedWav;
            _succeed = succeed;
        }

        public Task<YouTubeToolStatus> CheckToolStatusAsync(CancellationToken ct = default)
        {
            CheckToolStatusCalled = true;
            return Task.FromResult(YouTubeToolStatus.Available(@"C:\tools\yt-dlp.exe", "2024.08.06"));
        }

        public Task<YouTubeMetadata> ProbeMetadataAsync(string url, CancellationToken ct = default)
        {
            return Task.FromResult(new YouTubeMetadata("dQw4w9WgXcQ", "Song Title", 180, "Artist Channel", url));
        }

        public Task<YouTubeIngestResult> IngestYouTubeAsync(YouTubeIngestRequest request, IProgress<YouTubeDownloadProgress>? progress = null, CancellationToken ct = default)
        {
            IngestCalled = true;
            if (!_succeed)
            {
                return Task.FromResult(YouTubeIngestResult.Failed(request.JobId, "Download failed"));
            }

            File.WriteAllBytes(_normalizedWav, new byte[] { 1, 2, 3 });
            var audioMeta = new AudioMetadata(_normalizedWav, "wav", "pcm_s16le", 180, 22050, 1, 128000, 1024, 1);
            var audioRes = AudioIngestResult.Successful(request.JobId, _normalizedWav, _normalizedWav, audioMeta);

            return Task.FromResult(YouTubeIngestResult.Successful(
                request.JobId,
                "dQw4w9WgXcQ",
                request.Url,
                "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
                "Song Title",
                "Artist Channel",
                180,
                null,
                _normalizedWav,
                audioRes
            ));
        }
    }

    private class MockFfmpegLocator : IFfmpegToolLocator
    {
        public Task<FfmpegToolInfo> LocateToolsAsync(string? explicitFfmpegPath = null, string? explicitFfprobePath = null, CancellationToken ct = default)
        {
            return Task.FromResult(new FfmpegToolInfo(@"C:\tools\ffmpeg.exe", @"C:\tools\ffprobe.exe", true, true, "ffmpeg 6.0", "ffprobe 6.0"));
        }
    }

    private class MockTranscriptionEngine : ITranscriptionEngine
    {
        private readonly MusicTimeline _timeline;

        public MockTranscriptionEngine(MusicTimeline timeline)
        {
            _timeline = timeline;
        }

        public Task<TranscriptionEngineStatus> CheckAvailabilityAsync(CancellationToken ct = default)
        {
            return Task.FromResult(TranscriptionEngineStatus.Available(@"C:\python.exe", "3.11.2", "0.4.0"));
        }

        public Task<TranscriptionResult> TranscribeAsync(TranscriptionRequest request, IProgress<TranscriptionProgress>? progress = null, CancellationToken ct = default)
        {
            return Task.FromResult(TranscriptionResult.Successful(
                request.JobId,
                request.NormalizedAudioPath,
                Path.Combine(Path.GetDirectoryName(request.NormalizedAudioPath)!, "transcription.mid"),
                _timeline,
                playableNoteCount: _timeline.Notes.Count,
                outOfRangeNoteCount: 0,
                minPitch: 60,
                maxPitch: 60,
                runtimeSeconds: 1.2
            ));
        }

        public void Dispose() { }
    }

    [Fact]
    public void AddYouTubeUrl_Valid_AddsQueueItem()
    {
        using var vm = new TranscribeViewModel();
        vm.YouTubeUrlInput = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";

        vm.AddYouTubeUrl();

        Assert.Single(vm.QueueItems);
        var item = vm.QueueItems[0];
        Assert.Equal(AudioSourceKind.YouTube, item.SourceKind);
        Assert.Equal("dQw4w9WgXcQ", item.VideoId);
        Assert.True(vm.HasItems);
    }

    [Fact]
    public void AddYouTubeUrl_Duplicate_Ignored()
    {
        using var vm = new TranscribeViewModel();
        vm.YouTubeUrlInput = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";
        vm.AddYouTubeUrl();

        // Add duplicate via youtu.be short link
        vm.YouTubeUrlInput = "https://youtu.be/dQw4w9WgXcQ";
        vm.AddYouTubeUrl();

        Assert.Single(vm.QueueItems);
    }

    [Fact]
    public void AddYouTubeUrl_PlaylistOnly_ShowsError()
    {
        using var vm = new TranscribeViewModel();
        vm.YouTubeUrlInput = "https://www.youtube.com/playlist?list=PL123456";
        vm.AddYouTubeUrl();

        Assert.Empty(vm.QueueItems);
        Assert.Contains(YouTubeError.PlaylistUnsupported, vm.SummaryText);
    }

    [Fact]
    public async Task StartIngestAsync_YouTubeItem_PreparesSuccessfully()
    {
        string normalizedWav = Path.Combine(_tempRoot, "normalized_yt.wav");
        var ytService = new MockYouTubeService(normalizedWav);
        var profileContext = new PianoProfileContext();

        using var vm = new TranscribeViewModel(
            toolLocator: new MockFfmpegLocator(),
            profileContext: profileContext,
            youtubeService: ytService
        );

        vm.YouTubeUrlInput = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";
        vm.AddYouTubeUrl();

        await vm.StartIngestAsync();

        var item = vm.QueueItems[0];
        Assert.True(item.IsPrepared);
        Assert.Equal("준비 완료", item.StatusText);
        Assert.Equal("Artist Channel - Song Title", item.FileName);
        Assert.True(item.CanStartAi);
    }

    [Fact]
    public async Task TranscribeYouTubeItem_UsesYouTubeTitleForLibraryScore()
    {
        string normalizedWav = Path.Combine(_tempRoot, "normalized_yt.wav");
        var ytService = new MockYouTubeService(normalizedWav);
        var profileContext = new PianoProfileContext();

        var timeline = new MusicTimeline("Song Title");
        timeline.AddNote(new NoteEvent(60, 0, 1000, 80));
        var engine = new MockTranscriptionEngine(timeline);

        using var vm = new TranscribeViewModel(
            toolLocator: new MockFfmpegLocator(),
            transcriptionEngine: engine,
            profileContext: profileContext,
            youtubeService: ytService
        );

        vm.YouTubeUrlInput = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";
        vm.AddYouTubeUrl();

        await vm.StartIngestAsync();
        var item = vm.QueueItems[0];

        await vm.StartAiTranscriptionAsync(item);

        Assert.True(item.IsAiCompleted);
        Assert.True(item.HasAiResult);
        Assert.Contains("Song Title", vm.SummaryText);
    }

    [Fact]
    public async Task ProfileChanged_UpdatesYouTubeTranscribedDiagnostics()
    {
        string normalizedWav = Path.Combine(_tempRoot, "normalized_yt.wav");
        var ytService = new MockYouTubeService(normalizedWav);
        var profileContext = new PianoProfileContext(); // 88-key default

        // Note 21 (A0) - valid on 88-key (21..108), out of range on 61-key (36..96)
        var timeline = new MusicTimeline("Song Title");
        timeline.AddNote(new NoteEvent(21, 0, 1000, 80));
        var engine = new MockTranscriptionEngine(timeline);

        using var vm = new TranscribeViewModel(
            toolLocator: new MockFfmpegLocator(),
            transcriptionEngine: engine,
            profileContext: profileContext,
            youtubeService: ytService
        );

        vm.YouTubeUrlInput = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";
        vm.AddYouTubeUrl();
        await vm.StartIngestAsync();
        var item = vm.QueueItems[0];
        await vm.StartAiTranscriptionAsync(item);

        Assert.Equal("총 1음 (연주 가능 1음)", item.NoteStatsText);

        // Switch to 61-key profile
        profileContext.SetKind(RobloxPianoProfileKind.Key61);

        // Diagnostics should now show 0 playable notes
        Assert.Equal("총 1음 (연주 가능 0음)", item.NoteStatsText);
    }
}
