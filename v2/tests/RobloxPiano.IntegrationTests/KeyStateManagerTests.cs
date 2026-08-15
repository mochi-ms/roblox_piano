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
}
