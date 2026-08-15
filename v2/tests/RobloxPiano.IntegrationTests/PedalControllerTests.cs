using RobloxPiano.Playback.Windows.Input;
using RobloxPiano.Playback.Windows.Playback;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class PedalControllerTests
{
    [Fact]
    public void PedalDown_SendsSpaceKeyDown_AndTracksState()
    {
        using var backend = new DryRunPlaybackBackend();
        using var pedal = new PedalController(backend);

        pedal.PedalDown();

        Assert.True(pedal.IsDown);
        Assert.Contains("space", backend.PressedKeys);
        Assert.Single(backend.Events);
        Assert.Equal(BackendAction.KeyDown, backend.Events[0].Action);
        Assert.Equal("space", backend.Events[0].Key);
    }

    [Fact]
    public void PedalUp_SendsSpaceKeyUp_AndResetsState()
    {
        using var backend = new DryRunPlaybackBackend();
        using var pedal = new PedalController(backend);

        pedal.PedalDown();
        pedal.PedalUp();

        Assert.False(pedal.IsDown);
        Assert.DoesNotContain("space", backend.PressedKeys);
        Assert.Equal(2, backend.Events.Count);
        Assert.Equal(BackendAction.KeyUp, backend.Events[1].Action);
        Assert.Equal("space", backend.Events[1].Key);
    }

    [Fact]
    public void RepeatedPedalDown_DoesNotSendDuplicateEvents()
    {
        using var backend = new DryRunPlaybackBackend();
        using var pedal = new PedalController(backend);

        pedal.PedalDown();
        pedal.PedalDown();

        Assert.True(pedal.IsDown);
        Assert.Single(backend.Events);
    }

    [Fact]
    public void Release_ReleasesHeldPedal()
    {
        using var backend = new DryRunPlaybackBackend();
        using var pedal = new PedalController(backend);

        pedal.PedalDown();
        pedal.Release();

        Assert.False(pedal.IsDown);
        Assert.Empty(backend.PressedKeys);
    }
}
