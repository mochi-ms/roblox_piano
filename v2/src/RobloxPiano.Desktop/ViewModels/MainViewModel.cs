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
        CurrentView = _playerViewModel;
    }

    [RelayCommand]
    private void Navigate(string viewName)
    {
        CurrentView = viewName switch
        {
            "Player" => _playerViewModel,
            "Library" => _libraryViewModel,
            "Transcribe" => _transcribeViewModel,
            "Settings" => _settingsViewModel,
            _ => _playerViewModel
        };
    }
}
