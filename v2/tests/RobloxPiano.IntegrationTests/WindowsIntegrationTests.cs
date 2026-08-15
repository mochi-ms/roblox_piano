using System.Diagnostics;
using RobloxPiano.Core.Music;
using RobloxPiano.Core.Piano;
using RobloxPiano.Desktop.ViewModels;
using RobloxPiano.Playback.Windows.Input;
using RobloxPiano.Playback.Windows.Playback;
using RobloxPiano.Playback.Windows.WindowsIntegration;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class WindowsIntegrationTests
{
    // ==========================================
    // 1. Roblox Target Window Tests
    // ==========================================

    [Fact]
    public void RobloxTargetWindow_OneValidWindow_AutoSelects()
    {
        var fakeApi = new FakeWindowApi();
        fakeApi.AddWindow(1001, 5001, "RobloxPlayerBeta", "Roblox");

        var service = new RobloxTargetWindowService(fakeApi);
        service.Refresh();

        Assert.True(service.HasTarget);
        Assert.NotNull(service.CurrentTarget);
        Assert.Equal((nint)1001, service.CurrentTarget.Hwnd);
        Assert.Equal(5001, service.CurrentTarget.ProcessId);
        Assert.Single(service.AvailableTargets);
    }

    [Fact]
    public void RobloxTargetWindow_NoWindows_NoTarget()
    {
        var fakeApi = new FakeWindowApi();
        fakeApi.AddWindow(1001, 5001, "Notepad", "Untitled - Notepad");

        var service = new RobloxTargetWindowService(fakeApi);
        service.Refresh();

        Assert.False(service.HasTarget);
        Assert.Null(service.CurrentTarget);
        Assert.Empty(service.AvailableTargets);
    }

    [Fact]
    public void RobloxTargetWindow_MultipleWindows_RequiresSelection()
    {
        var fakeApi = new FakeWindowApi();
        fakeApi.AddWindow(1001, 5001, "RobloxPlayerBeta", "Roblox - Game 1");
        fakeApi.AddWindow(1002, 5002, "RobloxPlayerBeta", "Roblox - Game 2");

        var service = new RobloxTargetWindowService(fakeApi);
        service.Refresh();

        // Must NOT silently choose arbitrary target
        Assert.False(service.HasTarget);
        Assert.Null(service.CurrentTarget);
        Assert.Equal(2, service.AvailableTargets.Count);

        // Explicit user selection succeeds
        bool selected = service.SetTarget(1002);
        Assert.True(selected);
        Assert.True(service.HasTarget);
        Assert.Equal((nint)1002, service.CurrentTarget!.Hwnd);
    }

    [Fact]
    public void RobloxTargetWindow_RejectsNonRobloxProcess()
    {
        var fakeApi = new FakeWindowApi();
        fakeApi.AddWindow(1001, 5001, "RobloxStudioBeta", "Roblox Studio - Place1");
        fakeApi.AddWindow(1002, 5002, "chrome", "Roblox - Google Chrome");

        var service = new RobloxTargetWindowService(fakeApi);
        service.Refresh();

        Assert.False(service.HasTarget);
        Assert.Empty(service.AvailableTargets);
    }

    [Fact]
    public void RobloxTargetWindow_RejectsRobloxPlayerLauncher()
    {
        var fakeApi = new FakeWindowApi();
        fakeApi.AddWindow(1001, 5001, "RobloxPlayerLauncher", "Roblox");

        var service = new RobloxTargetWindowService(fakeApi);
        service.Refresh();

        Assert.False(service.HasTarget);
        Assert.Null(service.CurrentTarget);
        Assert.Empty(service.AvailableTargets);
    }

    [Fact]
    public void RobloxTargetWindow_RejectsDestroyedWindow()
    {
        var fakeApi = new FakeWindowApi();
        var win = fakeApi.AddWindow(1001, 5001, "RobloxPlayerBeta", "Roblox");

        var service = new RobloxTargetWindowService(fakeApi);
        service.Refresh();
        Assert.True(service.HasTarget);

        // Window gets destroyed
        win.IsDestroyed = true;

        Assert.False(service.ValidateTarget());
        service.Refresh();
        Assert.False(service.HasTarget);
        Assert.Null(service.CurrentTarget);
    }

    [Fact]
    public void RobloxTargetWindow_PreservesSelectedTargetAcrossRefresh()
    {
        var fakeApi = new FakeWindowApi();
        fakeApi.AddWindow(1001, 5001, "RobloxPlayerBeta", "Roblox 1");
        fakeApi.AddWindow(1002, 5002, "RobloxPlayerBeta", "Roblox 2");

        var service = new RobloxTargetWindowService(fakeApi);
        service.Refresh();

        service.SetTarget(1002);
        Assert.Equal((nint)1002, service.CurrentTarget!.Hwnd);

        // Refresh should preserve 1002
        service.Refresh();
        Assert.True(service.HasTarget);
        Assert.Equal((nint)1002, service.CurrentTarget!.Hwnd);
    }

    [Fact]
    public void RobloxTargetWindow_TitleChange_DoesNotInvalidateValidTarget()
    {
        var fakeApi = new FakeWindowApi();
        var win = fakeApi.AddWindow(1001, 5001, "RobloxPlayerBeta", "Roblox - Menu");

        var service = new RobloxTargetWindowService(fakeApi);
        service.Refresh();
        Assert.True(service.HasTarget);

        // Title changes when user enters a game
        win.Title = "Roblox - Playing Piano Game";

        Assert.True(service.ValidateTarget());
        service.Refresh();

        Assert.True(service.HasTarget);
        Assert.Equal("Roblox - Playing Piano Game", service.CurrentTarget!.Title);
    }

    // ==========================================
    // 2. Playback Target Guard & Activation Tests
    // ==========================================

    [Fact]
    public async Task TargetGuard_ActivationSuccess_AllowsPlayback()
    {
        var fakeApi = new FakeWindowApi();
        fakeApi.AddWindow(1001, 5001, "RobloxPlayerBeta", "Roblox");
        fakeApi.ForegroundWindow = 9999; // Currently background app

        var service = new RobloxTargetWindowService(fakeApi);
        service.Refresh();

        using var guard = new PlaybackTargetGuard(service, fakeApi);

        bool activated = await guard.ActivateAndVerifyTargetAsync();
        Assert.True(activated);
        Assert.Equal((nint)1001, fakeApi.ForegroundWindow);
    }

    [Fact]
    public async Task TargetGuard_ActivationFailure_BlocksPlayback()
    {
        var fakeApi = new FakeWindowApi();
        fakeApi.AddWindow(1001, 5001, "RobloxPlayerBeta", "Roblox");
        fakeApi.AllowActivation = false; // Simulate OS refusing SetForegroundWindow

        var service = new RobloxTargetWindowService(fakeApi);
        service.Refresh();

        using var guard = new PlaybackTargetGuard(service, fakeApi);

        bool activated = await guard.ActivateAndVerifyTargetAsync();
        Assert.False(activated);
    }

    [Fact]
    public async Task TargetGuard_WrongForegroundAfterActivation_BlocksPlayback()
    {
        var fakeApi = new FakeWindowApi();
        fakeApi.AddWindow(1001, 5001, "RobloxPlayerBeta", "Roblox");
        fakeApi.ForegroundWindow = 8888; // Different foreground window
        fakeApi.AllowActivation = false;

        var service = new RobloxTargetWindowService(fakeApi);
        service.Refresh();

        using var guard = new PlaybackTargetGuard(service, fakeApi);

        bool activated = await guard.ActivateAndVerifyTargetAsync();
        Assert.False(activated);
    }

    [Fact]
    public async Task TargetGuard_MinimizedTarget_RestoresBeforeActivation()
    {
        var fakeApi = new FakeWindowApi();
        fakeApi.AddWindow(1001, 5001, "RobloxPlayerBeta", "Roblox", isVisible: true, isIconic: true);

        var service = new RobloxTargetWindowService(fakeApi);
        service.Refresh();

        using var guard = new PlaybackTargetGuard(service, fakeApi);

        bool activated = await guard.ActivateAndVerifyTargetAsync();
        Assert.True(activated);
        Assert.True(fakeApi.RestoreMinimizedCalled);
        Assert.False(fakeApi.Windows[1001].IsIconic);
    }

    [Fact]
    public async Task TargetGuard_InvalidTarget_BlocksPlayback()
    {
        var fakeApi = new FakeWindowApi();
        var service = new RobloxTargetWindowService(fakeApi);
        service.Refresh();

        using var guard = new PlaybackTargetGuard(service, fakeApi);

        bool activated = await guard.ActivateAndVerifyTargetAsync();
        Assert.False(activated);
    }

    // ==========================================
    // 3. Foreground Loss Safety Test
    // ==========================================

    [Fact]
    public async Task TargetGuard_ForegroundLostDuringPlayback_StopsAndReleasesAll()
    {
        var fakeApi = new FakeWindowApi();
        fakeApi.AddWindow(1001, 5001, "RobloxPlayerBeta", "Roblox");
        fakeApi.ForegroundWindow = 1001;

        var service = new RobloxTargetWindowService(fakeApi);
        service.Refresh();

        using var guard = new PlaybackTargetGuard(service, fakeApi);

        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper, defaultHoldDurationMs: 100);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;

        var timeline = new MusicTimeline("Long Song");
        for (int i = 0; i < 20; i++)
        {
            timeline.AddNote(new NoteEvent(60, i * 0.1, (i + 1) * 0.1));
        }

        scheduler.SetTimeline(timeline);
        scheduler.Play();

        bool focusLostTriggered = false;
        guard.StartMonitoring(() =>
        {
            focusLostTriggered = true;
            scheduler.Stop();
        });

        await Task.Delay(50);
        Assert.True(scheduler.HasActiveWorker || scheduler.State == PlaybackState.Playing);

        // User Alt+Tabs away to Chrome (HWND 7777)
        fakeApi.ForegroundWindow = 7777;

        // Wait for monitor interval (50ms)
        for (int i = 0; i < 20; i++)
        {
            if (focusLostTriggered) break;
            await Task.Delay(20);
        }

        Assert.True(focusLostTriggered);
        Assert.False(scheduler.HasActiveWorker);
        Assert.Equal(PlaybackState.Stopped, scheduler.State);
        Assert.Empty(keyState.ActiveKeys);
        Assert.Empty(keyState.ActiveModifiers);
    }

    // ==========================================
    // 4. Global Hotkey Tests & Target-Required Playback Tests
    // ==========================================

    [Fact]
    public async Task Hotkey_F6_StartsPlaybackWhenReady()
    {
        var fakeApi = new FakeWindowApi();
        fakeApi.AddWindow(1001, 5001, "RobloxPlayerBeta", "Roblox");
        fakeApi.ForegroundWindow = 1001;

        var targetService = new RobloxTargetWindowService(fakeApi);
        targetService.Refresh();
        var targetGuard = new PlaybackTargetGuard(targetService, fakeApi);

        using var backend = new DryRunPlaybackBackend();
        using var playerVm = new PlayerViewModel(backend, targetService, targetGuard);
        using var hotkeyService = new GlobalHotkeyService(fakeApi);
        using var mainVm = new MainViewModel(playerVm, null, null, null, hotkeyService);

        var timeline = new MusicTimeline("Hotkey Test");
        for (int i = 0; i < 10; i++)
        {
            timeline.AddNote(new NoteEvent(60, i * 0.5, (i + 1) * 0.5));
        }
        playerVm.LoadTimeline(timeline);
        playerVm.Scheduler.CountdownSeconds = 0;

        bool handled = false;
        hotkeyService.ProcessWindowMessage(100, GlobalHotkeyService.WM_HOTKEY, (nint)GlobalHotkeyService.HOTKEY_ID_F6, nint.Zero, ref handled);

        Assert.True(handled);
        for (int i = 0; i < 20; i++)
        {
            if (playerVm.Scheduler.State == PlaybackState.Playing) break;
            await Task.Delay(20);
        }

        Assert.Equal(PlaybackState.Playing, playerVm.Scheduler.State);
        playerVm.Stop();
    }

    [Fact]
    public async Task Hotkey_F6_NoScore_DoesNotStart()
    {
        var fakeApi = new FakeWindowApi();
        fakeApi.AddWindow(1001, 5001, "RobloxPlayerBeta", "Roblox");

        var targetService = new RobloxTargetWindowService(fakeApi);
        targetService.Refresh();
        var targetGuard = new PlaybackTargetGuard(targetService, fakeApi);

        using var backend = new DryRunPlaybackBackend();
        using var playerVm = new PlayerViewModel(backend, targetService, targetGuard);
        using var hotkeyService = new GlobalHotkeyService(fakeApi);
        using var mainVm = new MainViewModel(playerVm, null, null, null, hotkeyService);

        playerVm.Scheduler.CountdownSeconds = 0;

        bool handled = false;
        hotkeyService.ProcessWindowMessage(100, GlobalHotkeyService.WM_HOTKEY, (nint)GlobalHotkeyService.HOTKEY_ID_F6, nint.Zero, ref handled);

        Assert.True(handled);
        await Task.Delay(30);

        Assert.False(playerVm.IsPlaying);
        Assert.Equal(PlaybackState.Idle, playerVm.Scheduler.State);
    }

    [Fact]
    public async Task Hotkey_F7_PausesAndResumes()
    {
        var fakeApi = new FakeWindowApi();
        fakeApi.AddWindow(1001, 5001, "RobloxPlayerBeta", "Roblox");
        fakeApi.ForegroundWindow = 1001;

        var targetService = new RobloxTargetWindowService(fakeApi);
        targetService.Refresh();
        var targetGuard = new PlaybackTargetGuard(targetService, fakeApi);

        using var backend = new DryRunPlaybackBackend();
        using var playerVm = new PlayerViewModel(backend, targetService, targetGuard);
        using var hotkeyService = new GlobalHotkeyService(fakeApi);
        using var mainVm = new MainViewModel(playerVm, null, null, null, hotkeyService);

        var timeline = new MusicTimeline("Pause Resume Test");
        for (int i = 0; i < 10; i++)
        {
            timeline.AddNote(new NoteEvent(60, i * 0.5, (i + 1) * 0.5));
        }
        playerVm.LoadTimeline(timeline);
        playerVm.Scheduler.CountdownSeconds = 0;
        playerVm.Play();

        for (int i = 0; i < 20; i++)
        {
            if (playerVm.Scheduler.State == PlaybackState.Playing) break;
            await Task.Delay(20);
        }
        Assert.Equal(PlaybackState.Playing, playerVm.Scheduler.State);

        // F7 -> Pause
        bool handled = false;
        hotkeyService.ProcessWindowMessage(100, GlobalHotkeyService.WM_HOTKEY, (nint)GlobalHotkeyService.HOTKEY_ID_F7, nint.Zero, ref handled);
        Assert.True(handled);
        for (int i = 0; i < 20; i++)
        {
            if (playerVm.Scheduler.State == PlaybackState.Paused) break;
            await Task.Delay(20);
        }
        Assert.Equal(PlaybackState.Paused, playerVm.Scheduler.State);

        // F7 -> Resume
        handled = false;
        hotkeyService.ProcessWindowMessage(100, GlobalHotkeyService.WM_HOTKEY, (nint)GlobalHotkeyService.HOTKEY_ID_F7, nint.Zero, ref handled);
        Assert.True(handled);
        for (int i = 0; i < 20; i++)
        {
            if (playerVm.Scheduler.State == PlaybackState.Playing) break;
            await Task.Delay(20);
        }
        Assert.Equal(PlaybackState.Playing, playerVm.Scheduler.State);

        playerVm.Stop();
    }

    [Fact]
    public async Task Hotkey_Escape_StopsAndReleasesAll()
    {
        var fakeApi = new FakeWindowApi();
        fakeApi.AddWindow(1001, 5001, "RobloxPlayerBeta", "Roblox");
        fakeApi.ForegroundWindow = 1001;

        var targetService = new RobloxTargetWindowService(fakeApi);
        targetService.Refresh();
        var targetGuard = new PlaybackTargetGuard(targetService, fakeApi);

        using var backend = new DryRunPlaybackBackend();
        using var playerVm = new PlayerViewModel(backend, targetService, targetGuard);
        using var hotkeyService = new GlobalHotkeyService(fakeApi);
        using var mainVm = new MainViewModel(playerVm, null, null, null, hotkeyService);

        var timeline = new MusicTimeline("Stop Test");
        for (int i = 0; i < 10; i++)
        {
            timeline.AddNote(new NoteEvent(60, i * 0.5, (i + 1) * 0.5));
        }
        playerVm.LoadTimeline(timeline);
        playerVm.Scheduler.CountdownSeconds = 0;
        playerVm.Play();

        for (int i = 0; i < 20; i++)
        {
            if (playerVm.Scheduler.State == PlaybackState.Playing) break;
            await Task.Delay(20);
        }
        Assert.Equal(PlaybackState.Playing, playerVm.Scheduler.State);

        // ESC -> Stop
        bool handled = false;
        hotkeyService.ProcessWindowMessage(100, GlobalHotkeyService.WM_HOTKEY, (nint)GlobalHotkeyService.HOTKEY_ID_ESC, nint.Zero, ref handled);
        Assert.True(handled);
        for (int i = 0; i < 20; i++)
        {
            if (playerVm.Scheduler.State == PlaybackState.Stopped) break;
            await Task.Delay(20);
        }

        Assert.Equal(PlaybackState.Stopped, playerVm.Scheduler.State);
        Assert.False(playerVm.Scheduler.HasActiveWorker);
    }

    [Fact]
    public async Task Hotkey_Play_RequiresValidRobloxTargetForRealBackend()
    {
        var fakeApi = new FakeWindowApi(); // No Roblox windows
        var targetService = new RobloxTargetWindowService(fakeApi);
        targetService.Refresh();
        var targetGuard = new PlaybackTargetGuard(targetService, fakeApi);

        // Target-required fake backend (guarantees ZERO SendInput / Win32 keyup during tests)
        using var backend = new TargetRequiredFakePlaybackBackend();
        using var playerVm = new PlayerViewModel(backend, targetService, targetGuard);
        using var hotkeyService = new GlobalHotkeyService(fakeApi);
        using var mainVm = new MainViewModel(playerVm, null, null, null, hotkeyService);

        var timeline = new MusicTimeline("Target-Required Backend Hotkey Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 1.0));
        playerVm.LoadTimeline(timeline);
        playerVm.Scheduler.CountdownSeconds = 0;

        bool handled = false;
        hotkeyService.ProcessWindowMessage(100, GlobalHotkeyService.WM_HOTKEY, (nint)GlobalHotkeyService.HOTKEY_ID_F6, nint.Zero, ref handled);

        Assert.True(handled);
        await Task.Delay(30);

        // Must NOT start playback because target is missing!
        Assert.False(playerVm.IsPlaying);
        Assert.Equal(PlaybackState.Idle, playerVm.Scheduler.State);
        Assert.Contains("Roblox", playerVm.StatusText);
        Assert.Empty(backend.Events);
        Assert.Empty(backend.PressedKeys);
    }

    [Fact]
    public async Task Playback_TargetRequiredFakeBackend_ValidForeground_AllowsPlayback()
    {
        var fakeApi = new FakeWindowApi();
        fakeApi.AddWindow(1001, 5001, "RobloxPlayerBeta", "Roblox");
        fakeApi.ForegroundWindow = 1001;

        var targetService = new RobloxTargetWindowService(fakeApi);
        targetService.Refresh();
        var targetGuard = new PlaybackTargetGuard(targetService, fakeApi);

        using var backend = new TargetRequiredFakePlaybackBackend();
        using var playerVm = new PlayerViewModel(backend, targetService, targetGuard);

        var timeline = new MusicTimeline("Positive Target-Required Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.2));
        playerVm.LoadTimeline(timeline);
        playerVm.Scheduler.CountdownSeconds = 0;

        await playerVm.PlayAsync();

        // Allow chord event to process
        for (int i = 0; i < 20; i++)
        {
            if (backend.Events.Count > 0) break;
            await Task.Delay(15);
        }

        Assert.True(targetService.HasTarget);
        Assert.NotEmpty(backend.Events);
        Assert.Contains(backend.Events, e => e.Action == BackendAction.KeyDown && e.Key == "t"); // C4 -> 't' on 61-key mapper
        playerVm.Stop();
    }

    [Fact]
    public void Hotkey_RegistrationFailure_DoesNotCrashApp()
    {
        var fakeApi = new FakeWindowApi { FailHotKeyRegistration = true };
        using var hotkeyService = new GlobalHotkeyService(fakeApi);

        bool success = hotkeyService.RegisterHotkeys(100);

        Assert.False(success);
        Assert.False(hotkeyService.RegistrationStatus[HotkeyAction.Play]);
        Assert.False(hotkeyService.RegistrationStatus[HotkeyAction.PauseResume]);
        Assert.False(hotkeyService.RegistrationStatus[HotkeyAction.Stop]);
    }

    [Fact]
    public void Hotkey_CommandFailure_DoesNotCrashMessageDispatch()
    {
        var fakeApi = new FakeWindowApi();
        using var hotkeyService = new GlobalHotkeyService(fakeApi);
        using var backend = new DryRunPlaybackBackend();
        using var playerVm = new PlayerViewModel(backend);
        using var mainVm = new MainViewModel(playerVm, null, null, null, hotkeyService);

        // Dispose playerVm early to simulate exception on hotkey handling
        playerVm.Dispose();

        bool handled = false;
        // Dispatching hotkey message must not throw unhandled exception
        hotkeyService.ProcessWindowMessage(100, GlobalHotkeyService.WM_HOTKEY, (nint)GlobalHotkeyService.HOTKEY_ID_F6, nint.Zero, ref handled);

        Assert.True(handled);
    }

    // ==========================================
    // 5. Overlay Tests
    // ==========================================

    [Fact]
    public void Overlay_Playing_ShowsSongAndTime()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;

        using var overlayVm = new OverlayViewModel(scheduler);
        overlayVm.UpdateScoreTitle("River Flows in You");

        var timeline = new MusicTimeline("River Flows in You");
        timeline.AddNote(new NoteEvent(60, 0.0, 5.0));
        scheduler.SetTimeline(timeline);

        scheduler.Play();

        Assert.True(overlayVm.IsVisible);
        Assert.Equal("재생 중", overlayVm.StatusText);
        Assert.Equal("River Flows in You", overlayVm.Title);

        scheduler.Stop();
    }

    [Fact]
    public void Overlay_Paused_ShowsPausedState()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;

        using var overlayVm = new OverlayViewModel(scheduler);
        var timeline = new MusicTimeline("Test Song");
        timeline.AddNote(new NoteEvent(60, 0.0, 5.0));
        scheduler.SetTimeline(timeline);

        scheduler.Play();
        scheduler.Pause();

        Assert.True(overlayVm.IsVisible);
        Assert.Equal("일시정지", overlayVm.StatusText);

        scheduler.Stop();
    }

    [Fact]
    public void Overlay_Stop_HidesOrShowsStoppedAccordingToPolicy()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper);
        using var scheduler = new PlaybackScheduler(engine, keyState);
        scheduler.CountdownSeconds = 0;

        using var overlayVm = new OverlayViewModel(scheduler);
        var timeline = new MusicTimeline("Test Song");
        timeline.AddNote(new NoteEvent(60, 0.0, 5.0));
        scheduler.SetTimeline(timeline);

        scheduler.Play();
        Assert.True(overlayVm.IsVisible);

        scheduler.Stop();
        Assert.False(overlayVm.IsVisible);
        Assert.Equal("정지됨", overlayVm.StatusText);
    }

    [Fact]
    public void Overlay_UpdatesSpeed()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper);
        using var scheduler = new PlaybackScheduler(engine, keyState);

        using var overlayVm = new OverlayViewModel(scheduler);
        overlayVm.UpdateSpeed(1.5);

        Assert.Equal("1.5x", overlayVm.FormattedSpeed);
    }

    [Fact]
    public void Overlay_DoesNotOwnPlaybackState()
    {
        using var backend = new DryRunPlaybackBackend();
        using var keyState = new KeyStateManager(backend);
        var mapper = new RobloxPianoMapper();
        var engine = new ChordEngine(keyState, mapper);
        using var scheduler = new PlaybackScheduler(engine, keyState);

        using var overlayVm = new OverlayViewModel(scheduler);

        // Overlay is passive: disposing overlay does NOT affect scheduler
        overlayVm.Dispose();

        var timeline = new MusicTimeline("Test");
        timeline.AddNote(new NoteEvent(60, 0.0, 0.1));
        scheduler.SetTimeline(timeline);

        scheduler.Play();
        Assert.True(scheduler.HasActiveWorker || scheduler.State == PlaybackState.Playing);
        scheduler.Stop();
    }

    // ==========================================
    // Fake Window API & Target-Required Fake Backend
    // ==========================================

    public class TargetRequiredFakePlaybackBackend : IPlaybackBackend, ITargetedPlaybackBackend
    {
        private readonly List<PlaybackBackendEvent> _events = new();
        private readonly HashSet<string> _pressedKeys = new();
        private readonly object _lock = new();

        public IReadOnlyList<PlaybackBackendEvent> Events
        {
            get { lock (_lock) return _events.ToList(); }
        }

        public IReadOnlyCollection<string> PressedKeys
        {
            get { lock (_lock) return _pressedKeys.ToList(); }
        }

        public void KeyDown(string key)
        {
            lock (_lock)
            {
                _events.Add(new PlaybackBackendEvent(Stopwatch.GetTimestamp(), BackendAction.KeyDown, key));
                _pressedKeys.Add(key);
            }
        }

        public void KeyUp(string key)
        {
            lock (_lock)
            {
                _events.Add(new PlaybackBackendEvent(Stopwatch.GetTimestamp(), BackendAction.KeyUp, key));
                _pressedKeys.Remove(key);
            }
        }

        public void ReleaseAll()
        {
            lock (_lock)
            {
                foreach (var k in _pressedKeys.ToList())
                {
                    _events.Add(new PlaybackBackendEvent(Stopwatch.GetTimestamp(), BackendAction.KeyUp, k));
                }
                _pressedKeys.Clear();
            }
        }

        public void Dispose()
        {
            ReleaseAll();
        }
    }

    public class FakeWindowApi : IWindowApi
    {
        public class FakeWindow
        {
            public nint Hwnd { get; set; }
            public int ProcessId { get; set; }
            public string ProcessName { get; set; } = "RobloxPlayerBeta";
            public string Title { get; set; } = "Roblox";
            public string ClassName { get; set; } = "WINDOWSCLIENT";
            public bool IsVisible { get; set; } = true;
            public bool IsIconic { get; set; } = false;
            public bool IsDestroyed { get; set; } = false;
        }

        public Dictionary<nint, FakeWindow> Windows { get; } = new();
        public HashSet<int> RunningProcesses { get; } = new();
        public nint ForegroundWindow { get; set; } = nint.Zero;
        public bool AllowActivation { get; set; } = true;
        public bool RestoreMinimizedCalled { get; private set; }

        public Dictionary<int, (nint hwnd, uint fsModifiers, uint vk)> RegisteredHotkeys { get; } = new();
        public bool FailHotKeyRegistration { get; set; }

        public FakeWindow AddWindow(nint hwnd, int pid, string processName = "RobloxPlayerBeta", string title = "Roblox", bool isVisible = true, bool isIconic = false)
        {
            var fw = new FakeWindow
            {
                Hwnd = hwnd,
                ProcessId = pid,
                ProcessName = processName,
                Title = title,
                IsVisible = isVisible,
                IsIconic = isIconic
            };
            Windows[hwnd] = fw;
            RunningProcesses.Add(pid);
            return fw;
        }

        public IEnumerable<nint> EnumTopLevelWindows() => Windows.Values.Where(w => !w.IsDestroyed).Select(w => w.Hwnd).ToList();

        public bool IsWindow(nint hwnd) => Windows.TryGetValue(hwnd, out var w) && !w.IsDestroyed;

        public bool IsWindowVisible(nint hwnd) => Windows.TryGetValue(hwnd, out var w) && !w.IsDestroyed && w.IsVisible;

        public string GetWindowTitle(nint hwnd) => Windows.TryGetValue(hwnd, out var w) && !w.IsDestroyed ? w.Title : string.Empty;

        public string GetWindowClassName(nint hwnd) => Windows.TryGetValue(hwnd, out var w) && !w.IsDestroyed ? w.ClassName : string.Empty;

        public int GetWindowProcessId(nint hwnd) => Windows.TryGetValue(hwnd, out var w) && !w.IsDestroyed ? w.ProcessId : 0;

        public string GetProcessName(int processId) => Windows.Values.FirstOrDefault(w => w.ProcessId == processId && !w.IsDestroyed)?.ProcessName ?? string.Empty;

        public bool IsProcessRunning(int processId) => RunningProcesses.Contains(processId);

        public nint GetForegroundWindow() => ForegroundWindow;

        public bool SetForegroundWindow(nint hwnd)
        {
            if (AllowActivation && Windows.ContainsKey(hwnd) && !Windows[hwnd].IsDestroyed)
            {
                ForegroundWindow = hwnd;
                return true;
            }
            return false;
        }

        public bool ShowWindow(nint hwnd, int nCmdShow)
        {
            if (Windows.TryGetValue(hwnd, out var w))
            {
                if (nCmdShow == Win32WindowApi.SW_RESTORE)
                {
                    w.IsIconic = false;
                    RestoreMinimizedCalled = true;
                }
                return true;
            }
            return false;
        }

        public bool IsIconic(nint hwnd) => Windows.TryGetValue(hwnd, out var w) && w.IsIconic;

        public bool RegisterHotKey(nint hwnd, int id, uint fsModifiers, uint vk)
        {
            if (FailHotKeyRegistration) return false;
            RegisteredHotkeys[id] = (hwnd, fsModifiers, vk);
            return true;
        }

        public bool UnregisterHotKey(nint hwnd, int id)
        {
            return RegisteredHotkeys.Remove(id);
        }
    }
}
