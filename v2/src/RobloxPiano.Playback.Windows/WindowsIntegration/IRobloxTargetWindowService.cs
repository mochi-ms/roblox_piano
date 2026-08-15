namespace RobloxPiano.Playback.Windows.WindowsIntegration;

public interface IRobloxTargetWindowService
{
    RobloxWindowInfo? CurrentTarget { get; }
    bool HasTarget { get; }
    IReadOnlyList<RobloxWindowInfo> AvailableTargets { get; }

    IReadOnlyList<RobloxWindowInfo> FindRobloxWindows();
    RobloxWindowInfo? FindBestTarget();
    void Refresh();
    bool SetTarget(nint hwnd);
    void ClearTarget();
    bool ValidateTarget(RobloxWindowInfo? target = null);

    event EventHandler? TargetChanged;
    event EventHandler? AvailableTargetsChanged;
}
