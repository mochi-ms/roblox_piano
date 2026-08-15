using RobloxPiano.Playback.Windows.Input;
using RobloxPiano.Playback.Windows.Playback;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class KeyStateManagerTests
{
    [Fact]
    public void PressAndReleasePhysicalKey_TracksActiveStateAccurately()
    {
        using var backend = new DryRunPlaybackBackend();
        using var manager = new KeyStateManager(backend);

        manager.PressPhysicalKey("q");
        manager.PressPhysicalKey("w");

        Assert.Contains("q", manager.ActiveKeys);
        Assert.Contains("w", manager.ActiveKeys);
        Assert.Contains("q", backend.PressedKeys);
        Assert.Contains("w", backend.PressedKeys);

        manager.ReleasePhysicalKey("q");
        Assert.DoesNotContain("q", manager.ActiveKeys);
        Assert.Contains("w", manager.ActiveKeys);

        manager.ReleaseAll();
        Assert.Empty(manager.ActiveKeys);
        Assert.Empty(backend.PressedKeys);
    }

    [Fact]
    public void SetModifier_TracksModifierState()
    {
        using var backend = new DryRunPlaybackBackend();
        using var manager = new KeyStateManager(backend);

        manager.SetModifier("SHIFT", true);
        Assert.Contains("SHIFT", manager.ActiveModifiers);
        Assert.Contains("shift", backend.PressedKeys);

        manager.SetModifier("CTRL", true);
        Assert.Contains("CTRL", manager.ActiveModifiers);
        Assert.Contains("ctrl", backend.PressedKeys);

        manager.SetModifier("SHIFT", false);
        Assert.DoesNotContain("SHIFT", manager.ActiveModifiers);
        Assert.Contains("CTRL", manager.ActiveModifiers);

        manager.ReleaseAll();
        Assert.Empty(manager.ActiveModifiers);
        Assert.Empty(backend.PressedKeys);
    }

    [Fact]
    public void EmergencyReleaseAll_ReleasesKeysAndModifiersImmediately()
    {
        using var backend = new DryRunPlaybackBackend();
        using var manager = new KeyStateManager(backend);

        manager.PressPhysicalKey("q");
        manager.PressPhysicalKey("w");
        manager.SetModifier("SHIFT", true);

        Assert.Equal(2, manager.ActiveKeys.Count);
        Assert.Single(manager.ActiveModifiers);

        manager.ReleaseAll();

        Assert.Empty(manager.ActiveKeys);
        Assert.Empty(manager.ActiveModifiers);
        Assert.Empty(backend.PressedKeys);
    }

    [Fact]
    public async Task Watchdog_ReleasesKeysOnInactivity()
    {
        using var backend = new DryRunPlaybackBackend();
        // Fast watchdog timeout 0.05s (50ms)
        using var manager = new KeyStateManager(backend, idleTimeoutSeconds: 0.05, enableWatchdog: true);

        manager.PressPhysicalKey("q");
        manager.SetModifier("SHIFT", true);

        Assert.Contains("q", manager.ActiveKeys);
        Assert.Contains("SHIFT", manager.ActiveModifiers);

        // Wait 600ms for watchdog check to trigger
        await Task.Delay(600);

        Assert.Empty(manager.ActiveKeys);
        Assert.Empty(manager.ActiveModifiers);
        Assert.Empty(backend.PressedKeys);
    }

    [Fact]
    public async Task KeyStateManager_BlockedKeyDown_ReleaseAllCannotLeaveLateStuckKey()
    {
        var blockingBackend = new PlaybackSchedulerTests.ControlledBlockingPlaybackBackend();
        using var manager = new KeyStateManager(blockingBackend);

        blockingBackend.SetBlockKey("t");

        // Start KeyDown in background task (which enters block)
        var keyDownTask = Task.Run(() => manager.PressPhysicalKey("t"));

        await blockingBackend.WaitForBlockEnteredAsync();

        // ReleaseAll is called while KeyDown is blocked
        manager.ReleaseAll();

        Assert.Empty(manager.ActiveKeys);

        // Unblock backend KeyDown
        blockingBackend.ReleaseBlock();
        await keyDownTask;

        // When KeyDown finishes, epoch check must undo the late KeyDown
        Assert.Empty(manager.ActiveKeys);
        Assert.Empty(blockingBackend.PressedKeys);
    }

    [Fact]
    public async Task KeyStateManager_BlockedModifierDown_ReleaseAllCannotLeaveLateStuckModifier()
    {
        var blockingBackend = new PlaybackSchedulerTests.ControlledBlockingPlaybackBackend();
        using var manager = new KeyStateManager(blockingBackend);

        blockingBackend.SetBlockKey("shift");

        var modTask = Task.Run(() => manager.SetModifier("SHIFT", true));

        await blockingBackend.WaitForBlockEnteredAsync();

        // ReleaseAll is called while SetModifier KeyDown is blocked
        manager.ReleaseAll();

        Assert.Empty(manager.ActiveModifiers);

        // Unblock backend KeyDown
        blockingBackend.ReleaseBlock();
        await modTask;

        // When SetModifier finishes, epoch check must undo the late KeyDown
        Assert.Empty(manager.ActiveModifiers);
        Assert.Empty(blockingBackend.PressedKeys);
    }

    [Fact]
    public void KeyStateManager_ModifierKeyDownFailure_RollsBackAndPropagates()
    {
        var backend = new ActionFailingPlaybackBackend { FailOnKeyDown = true, FailKeyDownKey = "shift" };
        using var manager = new KeyStateManager(backend);

        Assert.Throws<InvalidOperationException>(() => manager.SetModifier("SHIFT", true));

        Assert.DoesNotContain("SHIFT", manager.ActiveModifiers);
        Assert.Empty(backend.PressedKeys);
    }

    [Fact]
    public void KeyStateManager_ModifierKeyUpFailure_PropagatesAndEmergencyReleaseRemainsPossible()
    {
        var backend = new ActionFailingPlaybackBackend { FailOnKeyUp = true, FailKeyUpKey = "shift", FailKeyUpCount = 1 };
        using var manager = new KeyStateManager(backend);

        manager.SetModifier("SHIFT", true);
        Assert.Contains("SHIFT", manager.ActiveModifiers);
        Assert.Contains("shift", backend.PressedKeys);

        // Normal modifier release throws
        Assert.Throws<InvalidOperationException>(() => manager.SetModifier("SHIFT", false));

        // State remains intact so emergency ReleaseAll knows about it
        Assert.Contains("SHIFT", manager.ActiveModifiers);

        // Disable failure for emergency release
        backend.FailOnKeyUp = false;
        manager.ReleaseAll();

        Assert.Empty(manager.ActiveModifiers);
        Assert.Empty(backend.PressedKeys);
    }

    [Fact]
    public void KeyStateManager_PhysicalKeyUpFailure_PropagatesAndEmergencyReleaseRemainsPossible()
    {
        var backend = new ActionFailingPlaybackBackend { FailOnKeyUp = true, FailKeyUpKey = "q", FailKeyUpCount = 1 };
        using var manager = new KeyStateManager(backend);

        manager.PressPhysicalKey("q");
        Assert.Contains("q", manager.ActiveKeys);
        Assert.Contains("q", backend.PressedKeys);

        // Normal physical key release throws
        Assert.Throws<InvalidOperationException>(() => manager.ReleasePhysicalKey("q"));

        // State remains intact so emergency ReleaseAll knows about it
        Assert.Contains("q", manager.ActiveKeys);

        // Emergency ReleaseAll
        backend.FailOnKeyUp = false;
        manager.ReleaseAll();

        Assert.Empty(manager.ActiveKeys);
        Assert.Empty(backend.PressedKeys);
    }

    internal class ActionFailingPlaybackBackend : IPlaybackBackend
    {
        public bool FailOnKeyDown { get; set; }
        public string? FailKeyDownKey { get; set; }
        public bool FailOnKeyUp { get; set; }
        public string? FailKeyUpKey { get; set; }
        public int FailKeyUpCount { get; set; } = -1;
        private int _keyUpCounter;

        public HashSet<string> PressedKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void KeyDown(string key)
        {
            if (FailOnKeyDown && (FailKeyDownKey == null || string.Equals(key, FailKeyDownKey, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Simulated KeyDown failure for key {key}");
            }
            lock (PressedKeys)
            {
                PressedKeys.Add(key);
            }
        }

        public void KeyUp(string key)
        {
            _keyUpCounter++;
            if (FailOnKeyUp && (FailKeyUpKey == null || string.Equals(key, FailKeyUpKey, StringComparison.OrdinalIgnoreCase)))
            {
                if (FailKeyUpCount < 0 || _keyUpCounter == FailKeyUpCount)
                {
                    throw new InvalidOperationException($"Simulated KeyUp failure for key {key}");
                }
            }
            lock (PressedKeys)
            {
                PressedKeys.Remove(key);
            }
        }

        public void ReleaseAll()
        {
            lock (PressedKeys)
            {
                PressedKeys.Clear();
            }
        }

        public void Dispose() { }
    }
}
