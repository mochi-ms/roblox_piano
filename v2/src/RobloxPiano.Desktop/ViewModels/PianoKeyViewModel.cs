using CommunityToolkit.Mvvm.ComponentModel;

namespace RobloxPiano.Desktop.ViewModels;

public partial class PianoKeyViewModel : ObservableObject
{
    public int Pitch { get; init; }
    public string NoteName { get; init; } = string.Empty;
    public bool IsBlack { get; init; }
    public double KeyLeft { get; init; }
    public double KeyWidth { get; init; }
    public double KeyHeight { get; init; }
    public int ZIndex { get; init; }

    [ObservableProperty]
    private bool _isActive;
}
