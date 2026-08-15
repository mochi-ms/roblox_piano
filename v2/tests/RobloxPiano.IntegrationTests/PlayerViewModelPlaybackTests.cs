using RobloxPiano.Core.Library;
using RobloxPiano.Core.Music;
using RobloxPiano.Desktop.ViewModels;
using RobloxPiano.Playback.Windows.Input;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class PlayerViewModelPlaybackTests : IDisposable
{
    private readonly string _tempDir;

    public PlayerViewModelPlaybackTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"player_vm_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public void Player_DefaultProfile_Is88Key()
    {
        using var backend = new DryRunPlaybackBackend();
        using var vm = new PlayerViewModel(backend);

        Assert.Equal("Roblox 88키 (기본)", vm.SelectedPianoProfile);
        Assert.Equal(88, vm.CurrentPianoProfile.Keys.Count);
        Assert.Equal(21, vm.Mapper.MinPitch);
        Assert.Equal(108, vm.Mapper.MaxPitch);
    }

    [Fact]
    public void Player_88Key_VisualKeyboardContains21And108()
    {
        using var backend = new DryRunPlaybackBackend();
        using var vm = new PlayerViewModel(backend);

        Assert.Contains(vm.PianoKeys, k => k.Pitch == 21);  // A0
        Assert.Contains(vm.PianoKeys, k => k.Pitch == 108); // C8
        Assert.Equal(88, vm.PianoKeys.Count);
    }

    [Fact]
    public void Player_61Key_VisualKeyboardUses61Profile()
    {
        using var backend = new DryRunPlaybackBackend();
        using var vm = new PlayerViewModel(backend);

        vm.SelectedPianoProfile = "Roblox 61키";

        Assert.DoesNotContain(vm.PianoKeys, k => k.Pitch == 21);
        Assert.DoesNotContain(vm.PianoKeys, k => k.Pitch == 108);
        Assert.Contains(vm.PianoKeys, k => k.Pitch == 36); // C2
        Assert.Contains(vm.PianoKeys, k => k.Pitch == 96); // C7
        Assert.Equal(61, vm.PianoKeys.Count);
        Assert.Equal(36, vm.Mapper.MinPitch);
        Assert.Equal(96, vm.Mapper.MaxPitch);
    }

    [Fact]
    public void Player_ProfileSwitch_UpdatesMapperAndRebuildsKeyboard()
    {
        using var backend = new DryRunPlaybackBackend();
        using var vm = new PlayerViewModel(backend);

        Assert.True(vm.Mapper.CanPlay(21)); // 88-key

        vm.SelectedPianoProfile = "Roblox 61키";
        Assert.False(vm.Mapper.CanPlay(21)); // 61-key cannot play A0
        Assert.True(vm.Mapper.CanPlay(60));

        vm.SelectedPianoProfile = "Roblox 88키 (기본)";
        Assert.True(vm.Mapper.CanPlay(21));
        Assert.True(vm.Mapper.CanPlay(108));
    }

    [Fact]
    public async Task Player_ProfileSwitchDuringPlayback_StopsBeforeChange()
    {
        using var backend = new DryRunPlaybackBackend();
        using var vm = new PlayerViewModel(backend);

        var timeline = new MusicTimeline("Profile Switch Play Test");
        for (int i = 0; i < 20; i++)
        {
            timeline.AddNote(new NoteEvent(60, i * 0.1, (i + 1) * 0.1));
        }
        vm.LoadTimeline(timeline);
        vm.Scheduler.CountdownSeconds = 0;
        vm.Play();

        await Task.Delay(50);
        Assert.Equal(RobloxPiano.Playback.Windows.Playback.PlaybackState.Playing, vm.Scheduler.State);

        // Switch profile mid-playback
        vm.SelectedPianoProfile = "Roblox 61키";

        // Must be stopped
        Assert.Equal(RobloxPiano.Playback.Windows.Playback.PlaybackState.Stopped, vm.Scheduler.State);
        Assert.Empty(backend.PressedKeys);
    }

    [Fact]
    public async Task LoadScoreAsync_MmlScore_PopulatesMetadataAndTimeline()
    {
        string mmlFile = Path.Combine(_tempDir, "sample.mml");
        await File.WriteAllTextAsync(mmlFile, "MML@t120l4cdefgab>c;");

        var score = new ScoreItem("s-test", "Sample MML Song", "MML", "", mmlFile);

        using var backend = new DryRunPlaybackBackend();
        using var vm = new PlayerViewModel(backend);

        await vm.LoadScoreAsync(score);

        Assert.True(vm.HasScore);
        Assert.Equal("Sample MML Song", vm.Title);
        Assert.Equal("MML", vm.SourceType);
        Assert.Equal("120", vm.FormattedBpm);
        Assert.Equal("8", vm.FormattedTotalNotes);
        Assert.NotNull(vm.CurrentTimeline);
    }

    [Fact]
    public async Task LoadScoreAsync_NonExistentFile_SetsErrorStatus()
    {
        var score = new ScoreItem("s-missing", "Missing Song", "MIDI", "", Path.Combine(_tempDir, "non_existent.mid"));

        using var backend = new DryRunPlaybackBackend();
        using var vm = new PlayerViewModel(backend);

        await vm.LoadScoreAsync(score);

        Assert.False(vm.HasScore);
        Assert.Equal("악보 파일을 찾을 수 없습니다.", vm.StatusText);
    }

    [Fact]
    public void TransportCommands_PlayPauseStop_UpdatesStateAndStatusText()
    {
        using var backend = new DryRunPlaybackBackend();
        using var vm = new PlayerViewModel(backend);

        var timeline = new MusicTimeline("Transport Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 1.0));
        vm.LoadTimeline(timeline, "Transport Test", "MIDI");

        Assert.True(vm.HasScore);
        Assert.Equal("준비됨", vm.StatusText);

        vm.Scheduler.CountdownSeconds = 0;
        vm.Play();
        Assert.Equal(RobloxPiano.Playback.Windows.Playback.PlaybackState.Playing, vm.Scheduler.State);

        vm.Pause();
        Assert.Equal(RobloxPiano.Playback.Windows.Playback.PlaybackState.Paused, vm.Scheduler.State);

        vm.Stop();
        Assert.Equal(RobloxPiano.Playback.Windows.Playback.PlaybackState.Stopped, vm.Scheduler.State);
    }

    [Fact]
    public void SpeedAndTranspose_UpdatesSchedulerOptions()
    {
        using var backend = new DryRunPlaybackBackend();
        using var vm = new PlayerViewModel(backend);

        vm.SelectedSpeed = 1.5;
        Assert.Equal(1.5, vm.Scheduler.Speed);

        vm.SelectedTranspose = 2;
        Assert.Equal(2, vm.Scheduler.Transpose);
    }

    [Fact]
    public void PlayerViewModel_LoadTimeline_BuildsRealPianoRollNotes()
    {
        using var backend = new DryRunPlaybackBackend();
        using var vm = new PlayerViewModel(backend);

        var timeline = new MusicTimeline("Piano Roll Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.5, hand: HandType.Right));
        timeline.AddNote(new NoteEvent(64, 0.5, 1.0, hand: HandType.Right));
        timeline.AddNote(new NoteEvent(48, 0.0, 1.0, hand: HandType.Left));

        vm.LoadTimeline(timeline, "Piano Roll Test", "MIDI");

        Assert.Equal(3, vm.PianoRollNotes.Count);

        var n1 = vm.PianoRollNotes[0];
        Assert.Equal(60, n1.Pitch);
        Assert.Equal(0.0, n1.CanvasLeft);
        Assert.True(n1.Width > 0);
        Assert.Equal("#5B8DEF", n1.ColorBrushKey); // RH Accent

        var n3 = vm.PianoRollNotes[2];
        Assert.Equal(48, n3.Pitch);
        Assert.Equal("#34D399", n3.ColorBrushKey); // LH Success
    }

    [Fact]
    public async Task PlayerViewModel_RealChordLifecycle_ActivatesAndClearsPianoKey()
    {
        using var backend = new DryRunPlaybackBackend();
        using var vm = new PlayerViewModel(backend);

        var timeline = new MusicTimeline("Key Activation Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.15));

        vm.LoadTimeline(timeline);
        vm.Scheduler.CountdownSeconds = 0;

        var key60 = vm.PianoKeys.First(k => k.Pitch == 60);
        Assert.False(key60.IsActive);

        bool wasActiveDuringChord = false;
        vm.Scheduler.ChordStarted += (_, notes) =>
        {
            if (notes.Any(n => n.Pitch == 60))
            {
                wasActiveDuringChord = key60.IsActive;
            }
        };

        vm.Play();

        for (int i = 0; i < 30; i++)
        {
            if (vm.Scheduler.State == RobloxPiano.Playback.Windows.Playback.PlaybackState.Completed) break;
            if (key60.IsActive) wasActiveDuringChord = true;
            await Task.Delay(15);
        }

        Assert.True(wasActiveDuringChord, "Key 60 should have been active during chord execution");
        Assert.False(key60.IsActive, "Key 60 must be cleared after chord completion");
    }

    [Fact]
    public async Task PlayerViewModel_TransposedChordLifecycle_ActivatesTransposedPianoKey()
    {
        using var backend = new DryRunPlaybackBackend();
        using var vm = new PlayerViewModel(backend);

        var timeline = new MusicTimeline("Transpose Key Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.15)); // Pitch 60 + transpose 12 -> Pitch 72

        vm.LoadTimeline(timeline);
        vm.SelectedTranspose = 12;
        vm.Scheduler.CountdownSeconds = 0;

        var key60 = vm.PianoKeys.First(k => k.Pitch == 60);
        var key72 = vm.PianoKeys.First(k => k.Pitch == 72);

        bool was72Active = false;
        bool was60Active = false;

        vm.Scheduler.ChordStarted += (_, notes) =>
        {
            if (notes.Any(n => n.Pitch == 60))
            {
                was72Active = key72.IsActive;
                was60Active = key60.IsActive;
            }
        };

        vm.Play();

        for (int i = 0; i < 30; i++)
        {
            if (vm.Scheduler.State == RobloxPiano.Playback.Windows.Playback.PlaybackState.Completed) break;
            if (key72.IsActive) was72Active = true;
            if (key60.IsActive) was60Active = true;
            await Task.Delay(15);
        }

        Assert.True(was72Active, "Transposed key 72 should have been active");
        Assert.False(was60Active, "Untransposed key 60 must not be active");
    }

    [Fact]
    public async Task PlayerViewModel_ActualSchedulerProgressDoesNotCauseSeekFeedback()
    {
        using var backend = new DryRunPlaybackBackend();
        using var vm = new PlayerViewModel(backend);

        var timeline = new MusicTimeline("Progress Feedback Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.2));
        timeline.AddNote(new NoteEvent(64, 0.4, 0.6));

        vm.LoadTimeline(timeline);
        vm.Scheduler.CountdownSeconds = 0;

        int initialSeekCount = vm.Scheduler.SeekInvocationCount;
        vm.Play();

        // Allow real progress events to fire
        await Task.Delay(150);

        // Scheduler progress events must NOT have invoked Seek on scheduler
        Assert.Equal(initialSeekCount, vm.Scheduler.SeekInvocationCount);

        // Manually invoke user seek
        vm.Seek(0.1);
        Assert.Equal(initialSeekCount + 1, vm.Scheduler.SeekInvocationCount);

        vm.Stop();
    }

    [Fact]
    public async Task MainViewModel_Shutdown_FullyTerminatesPlaybackWorker()
    {
        using var backend = new DryRunPlaybackBackend();
        var playerVm = new PlayerViewModel(backend);
        var mainVm = new MainViewModel(playerVm);

        var timeline = new MusicTimeline("Shutdown Test");
        for (int i = 0; i < 30; i++)
        {
            timeline.AddNote(new NoteEvent(60, i * 0.1, (i + 1) * 0.1));
        }
        playerVm.LoadTimeline(timeline);
        playerVm.Scheduler.CountdownSeconds = 0;
        playerVm.Play();

        await Task.Delay(50);

        // Window / App shutdown triggers MainViewModel.Dispose()
        mainVm.Dispose();

        Assert.False(playerVm.Scheduler.HasActiveWorker);
        int eventsAtDispose = backend.Events.Count;
        await Task.Delay(100);

        Assert.Equal(eventsAtDispose, backend.Events.Count);
        Assert.Empty(backend.PressedKeys);
    }

    [Fact]
    public void DefaultProductionContext_Is88Key()
    {
        var context = new RobloxPiano.Core.Piano.PianoProfileContext();
        Assert.Equal(RobloxPiano.Core.Piano.RobloxPianoProfileKind.Key88, context.CurrentKind);
        Assert.Equal(88, context.CurrentProfile.Keys.Count);
    }

    [Fact]
    public void MainVM_UsesSingleSharedPianoProfileContext()
    {
        using var backend = new DryRunPlaybackBackend();
        using var playerVm = new PlayerViewModel(backend);
        using var mainVm = new MainViewModel(playerVm);

        Assert.Same(mainVm.ProfileContext, mainVm.PlayerViewModel.ProfileContext);
        Assert.Same(mainVm.ProfileContext, mainVm.ImportViewModel.ProfileContext);
        Assert.Same(mainVm.ProfileContext, mainVm.TranscribeViewModel.ProfileContext);
    }

    private class CapturingImportPipeline : RobloxPiano.Core.Importing.IImportPipeline
    {
        public RobloxPiano.Core.Importing.ImportRequest? LastRequest { get; private set; }

        public Task<RobloxPiano.Core.Importing.ImportResult> ImportFileAsync(RobloxPiano.Core.Importing.ImportRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            var tl = new MusicTimeline("Mock");
            tl.Notes.Add(new NoteEvent(21, 0, 1));
            return Task.FromResult(RobloxPiano.Core.Importing.ImportResult.Successful(request.FilePath, RobloxPiano.Core.Importing.ImportSourceType.Midi, "Mock", tl, 1, 0, 21, 21));
        }

        public Task<RobloxPiano.Core.Importing.ImportBatchResult> ImportBatchAsync(IReadOnlyList<RobloxPiano.Core.Importing.ImportRequest> requests, IProgress<(int Current, int Total, string FileName)>? progress = null, CancellationToken ct = default)
        {
            return Task.FromResult(new RobloxPiano.Core.Importing.ImportBatchResult(new List<RobloxPiano.Core.Importing.ImportResult>()));
        }
    }

    private class CapturingTranscriptionEngine : RobloxPiano.Core.Transcription.ITranscriptionEngine
    {
        public RobloxPiano.Core.Transcription.TranscriptionRequest? LastRequest { get; private set; }
        public int TranscribeCallCount { get; private set; }

        public Task<RobloxPiano.Core.Transcription.TranscriptionEngineStatus> CheckAvailabilityAsync(CancellationToken ct = default)
        {
            return Task.FromResult(RobloxPiano.Core.Transcription.TranscriptionEngineStatus.Available(@"C:\mock\python.exe", "3.11", "0.4.0"));
        }

        public Task<RobloxPiano.Core.Transcription.TranscriptionResult> TranscribeAsync(RobloxPiano.Core.Transcription.TranscriptionRequest request, IProgress<RobloxPiano.Core.Transcription.TranscriptionProgress>? progress = null, CancellationToken ct = default)
        {
            LastRequest = request;
            TranscribeCallCount++;
            var tl = new MusicTimeline("AiMock");
            tl.Notes.Add(new NoteEvent(21, 0, 1));
            return Task.FromResult(RobloxPiano.Core.Transcription.TranscriptionResult.Successful(request.JobId, request.NormalizedAudioPath, @"C:\mock\out.mid", tl, 1, 0, 21, 21, 0.5));
        }

        public void Dispose() { }
    }

    [Fact]
    public async Task ProfileSwitchTo61_ImportRequestUses61Profile()
    {
        var capturingPipeline = new CapturingImportPipeline();
        var context = new RobloxPiano.Core.Piano.PianoProfileContext();
        using var importVm = new ImportViewModel(pipeline: capturingPipeline, profileContext: context);

        context.SetKind(RobloxPiano.Core.Piano.RobloxPianoProfileKind.Key61);

        string testFile = Path.Combine(_tempDir, "test.mid");
        File.WriteAllBytes(testFile, new byte[] { 1, 2, 3 });
        importVm.AddFiles(new[] { testFile });

        await importVm.StartImportAsync();

        Assert.NotNull(capturingPipeline.LastRequest);
        Assert.NotNull(capturingPipeline.LastRequest.TargetPianoProfile);
        Assert.Equal(61, capturingPipeline.LastRequest.TargetPianoProfile.Keys.Count);
    }

    [Fact]
    public async Task ProfileSwitchTo88_ImportRequestUses88Profile()
    {
        var capturingPipeline = new CapturingImportPipeline();
        var context = new RobloxPiano.Core.Piano.PianoProfileContext();
        using var importVm = new ImportViewModel(pipeline: capturingPipeline, profileContext: context);

        context.SetKind(RobloxPiano.Core.Piano.RobloxPianoProfileKind.Key88);

        string testFile = Path.Combine(_tempDir, "test2.mid");
        File.WriteAllBytes(testFile, new byte[] { 1, 2, 3 });
        importVm.AddFiles(new[] { testFile });

        await importVm.StartImportAsync();

        Assert.NotNull(capturingPipeline.LastRequest);
        Assert.NotNull(capturingPipeline.LastRequest.TargetPianoProfile);
        Assert.Equal(88, capturingPipeline.LastRequest.TargetPianoProfile.Keys.Count);
    }

    [Fact]
    public async Task ProfileSwitchTo61_AiRequestUses61Profile()
    {
        var capturingEngine = new CapturingTranscriptionEngine();
        var context = new RobloxPiano.Core.Piano.PianoProfileContext();
        using var transcribeVm = new TranscribeViewModel(transcriptionEngine: capturingEngine, profileContext: context);

        context.SetKind(RobloxPiano.Core.Piano.RobloxPianoProfileKind.Key61);

        string testFile = Path.Combine(_tempDir, "audio.wav");
        File.WriteAllBytes(testFile, new byte[] { 1, 2, 3 });
        var item = new AudioQueueItemViewModel(testFile);
        var meta = new RobloxPiano.Core.Audio.AudioMetadata(testFile, "wav", "pcm_s16le", 1.0, 44100, 2, 1411200, 100, 1, "audio");
        item.SetPrepared(RobloxPiano.Core.Audio.AudioIngestResult.Successful(item.JobId, testFile, testFile, meta));

        await transcribeVm.StartAiTranscriptionAsync(item);

        Assert.NotNull(capturingEngine.LastRequest);
        Assert.NotNull(capturingEngine.LastRequest.TargetPianoProfile);
        Assert.Equal(61, capturingEngine.LastRequest.TargetPianoProfile.Keys.Count);
    }

    [Fact]
    public async Task ProfileSwitchTo88_AiRequestUses88Profile()
    {
        var capturingEngine = new CapturingTranscriptionEngine();
        var context = new RobloxPiano.Core.Piano.PianoProfileContext();
        using var transcribeVm = new TranscribeViewModel(transcriptionEngine: capturingEngine, profileContext: context);

        context.SetKind(RobloxPiano.Core.Piano.RobloxPianoProfileKind.Key88);

        string testFile = Path.Combine(_tempDir, "audio88.wav");
        File.WriteAllBytes(testFile, new byte[] { 1, 2, 3 });
        var item = new AudioQueueItemViewModel(testFile);
        var meta = new RobloxPiano.Core.Audio.AudioMetadata(testFile, "wav", "pcm_s16le", 1.0, 44100, 2, 1411200, 100, 1, "audio88");
        item.SetPrepared(RobloxPiano.Core.Audio.AudioIngestResult.Successful(item.JobId, testFile, testFile, meta));

        await transcribeVm.StartAiTranscriptionAsync(item);

        Assert.NotNull(capturingEngine.LastRequest);
        Assert.NotNull(capturingEngine.LastRequest.TargetPianoProfile);
        Assert.Equal(88, capturingEngine.LastRequest.TargetPianoProfile.Keys.Count);
    }

    [Fact]
    public void ProfileSwitch_RecomputesLoadedTimelineDiagnostics()
    {
        using var backend = new DryRunPlaybackBackend();
        using var vm = new PlayerViewModel(backend);

        var timeline = new MusicTimeline("Range Recompute Test");
        timeline.AddNote(new NoteEvent(21, 0.0, 1.0)); // A0
        timeline.AddNote(new NoteEvent(60, 0.0, 1.0)); // C4
        timeline.AddNote(new NoteEvent(108, 0.0, 1.0)); // C8

        vm.LoadTimeline(timeline);

        // In 88-key: all 3 playable, 0 out of range
        Assert.Contains("88키 기준", vm.PitchRangeText);
        Assert.Contains("3음 연주 가능", vm.PitchRangeText);
        Assert.Contains("범위 밖 0", vm.PitchRangeText);

        // Switch to 61-key: only 1 playable (pitch 60), 2 out of range (21, 108)
        vm.SelectedPianoProfile = "Roblox 61키";

        Assert.Contains("61키 기준", vm.PitchRangeText);
        Assert.Contains("1음 연주 가능", vm.PitchRangeText);
        Assert.Contains("범위 밖 2", vm.PitchRangeText);

        // Switch back to 88-key: all 3 playable
        vm.SelectedPianoProfile = "Roblox 88키 (기본)";

        Assert.Contains("88키 기준", vm.PitchRangeText);
        Assert.Contains("3음 연주 가능", vm.PitchRangeText);
        Assert.Contains("범위 밖 0", vm.PitchRangeText);
    }

    [Fact]
    public async Task ProfileSwitch_DoesNotRetranscribeAiResult()
    {
        var capturingEngine = new CapturingTranscriptionEngine();
        var context = new RobloxPiano.Core.Piano.PianoProfileContext();
        using var transcribeVm = new TranscribeViewModel(transcriptionEngine: capturingEngine, profileContext: context);

        string testFile = Path.Combine(_tempDir, "audio_no_retranscribe.wav");
        File.WriteAllBytes(testFile, new byte[] { 1, 2, 3 });
        var item = new AudioQueueItemViewModel(testFile);
        var meta = new RobloxPiano.Core.Audio.AudioMetadata(testFile, "wav", "pcm_s16le", 1.0, 44100, 2, 1411200, 100, 1, "audio_no_retranscribe");
        item.SetPrepared(RobloxPiano.Core.Audio.AudioIngestResult.Successful(item.JobId, testFile, testFile, meta));

        await transcribeVm.StartAiTranscriptionAsync(item);
        Assert.Equal(1, capturingEngine.TranscribeCallCount);

        // Profile changed
        context.SetKind(RobloxPiano.Core.Piano.RobloxPianoProfileKind.Key61);

        // TranscribeCallCount MUST NOT increase!
        Assert.Equal(1, capturingEngine.TranscribeCallCount);
    }
}
