namespace RobloxPiano.Playback.Windows.WindowsIntegration;

public record RobloxWindowInfo(
    nint Hwnd,
    int ProcessId,
    string ProcessName,
    string Title,
    string ClassName,
    bool IsVisible,
    bool IsMinimized
)
{
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Title)
            ? $"Roblox (PID {ProcessId})"
            : $"{Title} (PID {ProcessId})";
}
