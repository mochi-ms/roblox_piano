using RobloxPiano.Core.Music;

namespace RobloxPiano.Desktop.ViewModels;

public record PianoRollNoteViewModel(
    int Pitch,
    double StartTime,
    double Duration,
    HandType Hand,
    double CanvasLeft,
    double CanvasTop,
    double Width,
    double Height,
    string ColorBrushKey
);
