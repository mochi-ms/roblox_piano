using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RobloxPiano.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private object _currentView;

    private readonly PlayerViewModel _playerViewModel = new();
    private readonly LibraryViewModel _libraryViewModel = new();
    private readonly TranscribeViewModel _transcribeViewModel = new();
    private readonly SettingsViewModel _settingsViewModel = new();

    public MainViewModel()
    {
        _libraryViewModel.OpenScoreRequested += async (_, score) =>
        {
            await _playerViewModel.LoadScoreAsync(score);
            CurrentView = _playerViewModel;
        };
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
}
