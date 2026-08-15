namespace RobloxPiano.Playback.Windows.WindowsIntegration;

public interface IPlaybackTargetGuard : IDisposable
{
    IRobloxTargetWindowService TargetService { get; }
    bool IsMonitoring { get; }

    bool ValidateTarget();
    Task<bool> ActivateAndVerifyTargetAsync(CancellationToken ct = default);
    void StartMonitoring(Action onFocusLost);
    void StopMonitoring();

    event EventHandler? TargetLost;
    event EventHandler? TargetInvalidated;
}
