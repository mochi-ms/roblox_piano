using RobloxPiano.Playback.Windows.Input;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class PlaybackBackendTests
{
    [Theory]
    [InlineData("1", 0x02)]
    [InlineData("2", 0x03)]
    [InlineData("0", 0x0B)]
    [InlineData("q", 0x10)]
    [InlineData("w", 0x11)]
    [InlineData("p", 0x19)]
    [InlineData("a", 0x1E)]
    [InlineData("l", 0x26)]
    [InlineData("z", 0x2C)]
    [InlineData("m", 0x32)]
    [InlineData("shift", 0x2A)]
    [InlineData("ctrl", 0x1D)]
    [InlineData("alt", 0x38)]
    [InlineData("space", 0x39)]
    [InlineData("vk_20", 0x39)]
    [InlineData(";", 0x27)]
    [InlineData("=", 0x0D)]
    [InlineData(",", 0x33)]
    [InlineData("-", 0x0C)]
    [InlineData(".", 0x34)]
    [InlineData("/", 0x35)]
    [InlineData("`", 0x29)]
    [InlineData("[", 0x1A)]
    [InlineData("\\", 0x2B)]
    [InlineData("]", 0x1B)]
    [InlineData("'", 0x28)]
    public void VirtualKeyMap_ValidKeys_ReturnsExpectedScancodes(string key, ushort expectedScancode)
    {
        ushort scancode = VirtualKeyMap.GetScancode(key);
        Assert.Equal(expectedScancode, scancode);
    }

    [Fact]
    public void DryRunBackend_KeyDownAndKeyUp_MaintainsPressedStateAndRecordsEvents()
    {
        using var backend = new DryRunPlaybackBackend();

        backend.KeyDown("q");
        backend.KeyDown("SHIFT");

        Assert.Contains("q", backend.PressedKeys);
        Assert.Contains("shift", backend.PressedKeys);
        Assert.Equal(2, backend.Events.Count);
        Assert.Equal(BackendAction.KeyDown, backend.Events[0].Action);
        Assert.Equal("q", backend.Events[0].Key);
        Assert.Equal(BackendAction.KeyDown, backend.Events[1].Action);
        Assert.Equal("shift", backend.Events[1].Key);

        backend.KeyUp("q");
        Assert.DoesNotContain("q", backend.PressedKeys);
        Assert.Contains("shift", backend.PressedKeys);
        Assert.Equal(3, backend.Events.Count);
        Assert.Equal(BackendAction.KeyUp, backend.Events[2].Action);
        Assert.Equal("q", backend.Events[2].Key);

        backend.ReleaseAll();
        Assert.Empty(backend.PressedKeys);
        Assert.Equal(4, backend.Events.Count);
        Assert.Equal(BackendAction.KeyUp, backend.Events[3].Action);
        Assert.Equal("shift", backend.Events[3].Key);
    }
}
