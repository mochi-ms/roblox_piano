using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobloxPiano.Core.Library;
using RobloxPiano.Playback.Windows.WindowsIntegration;

namespace RobloxPiano.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    [ObservableProperty]
    private object _currentView;

    private readonly PlayerViewModel _playerViewModel;
    private readonly LibraryViewModel _libraryViewModel;
    private readonly TranscribeViewModel _transcribeViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly IGlobalHotkeyService _hotkeyService;
    private readonly EventHandler<ScoreItem> _openScoreHandler;
    private readonly EventHandler<HotkeyAction> _hotkeyHandler;

    private bool _disposed;

    public PlayerViewModel PlayerViewModel => _playerViewModel;
    public LibraryViewModel LibraryViewModel => _libraryViewModel;
    public TranscribeViewModel TranscribeViewModel => _transcribeViewModel;
    public SettingsViewModel SettingsViewModel => _settingsViewModel;
    public IGlobalHotkeyService HotkeyService => _hotkeyService;

    public MainViewModel(
        PlayerViewModel? playerViewModel = null,
        LibraryViewModel? libraryViewModel = null,
        TranscribeViewModel? transcribeViewModel = null,
        SettingsViewModel? settingsViewModel = null,
        IGlobalHotkeyService? hotkeyService = null)
    {
        _playerViewModel = playerViewModel ?? new PlayerViewModel();
        _libraryViewModel = libraryViewModel ?? new LibraryViewModel();
        _transcribeViewModel = transcribeViewModel ?? new TranscribeViewModel();
        _settingsViewModel = settingsViewModel ?? new SettingsViewModel();
        _hotkeyService = hotkeyService ?? new GlobalHotkeyService();

        _openScoreHandler = async (_, score) =>
        {
            await _playerViewModel.LoadScoreAsync(score);
            CurrentView = _playerViewModel;
        };

        _hotkeyHandler = async (_, action) =>
        {
            switch (action)
            {
                case HotkeyAction.Play:
                    await _playerViewModel.HandleHotkeyPlayAsync();
                    break;
                case HotkeyAction.PauseResume:
                    await _playerViewModel.HandleHotkeyPauseResumeAsync();
                    break;
                case HotkeyAction.Stop:
                    _playerViewModel.HandleHotkeyStop();
                    break;
            }
        };

        _libraryViewModel.OpenScoreRequested += _openScoreHandler;
        _hotkeyService.HotkeyPressed += _hotkeyHandler;
        CurrentView = _playerViewModel;
    }

    [RelayCommand]
    private void Navigate(string viewName)
    {
        CurrentView = viewName switch
        {
            "Player" or "플레이어" => _playerViewModel,
            "Library" or "라이브러리" => _libraryViewModel,
            "Transcribe" or "오디오 변환" => _transcribeViewModel,
            "Settings" or "설정" => _settingsViewModel,
            _ => _playerViewModel
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _libraryViewModel.OpenScoreRequested -= _openScoreHandler;
        _hotkeyService.HotkeyPressed -= _hotkeyHandler;
        _hotkeyService.Dispose();
        _playerViewModel.Dispose();
    }
}
