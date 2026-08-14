using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RobloxPiano.Desktop.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _selectedSection = "General";

    // General
    [ObservableProperty]
    private bool _openLastWorkspace = true;

    [ObservableProperty]
    private bool _checkForUpdates = true;

    [ObservableProperty]
    private int _defaultStartupPageIndex = 0;

    // Playback
    [ObservableProperty]
    private int _defaultSpeedIndex = 2;

    [ObservableProperty]
    private string _defaultTranspose = "0";

    [ObservableProperty]
    private int _sustainBehaviorIndex = 0;

    [ObservableProperty]
    private string _humanizeJitterMs = "5";

    // Roblox
    [ObservableProperty]
    private int _pianoLayoutIndex = 0;

    [ObservableProperty]
    private int _targetModeIndex = 0;

    [ObservableProperty]
    private bool _stopOnFocusLost = true;

    [ObservableProperty]
    private int _inputMethodIndex = 0;

    // Hotkeys
    [ObservableProperty]
    private string _hotkeyPlayPause = "F6";

    [ObservableProperty]
    private string _hotkeyStop = "Esc";

    [ObservableProperty]
    private string _hotkeyOverlay = "F4";

    [ObservableProperty]
    private string _hotkeyPanic = "F8";

    // Appearance
    [ObservableProperty]
    private int _themeIndex = 0;

    [ObservableProperty]
    private int _accentIndex = 0;

    [ObservableProperty]
    private int _uiScaleIndex = 0;

    // Advanced
    [ObservableProperty]
    private int _logLevelIndex = 0;

    [ObservableProperty]
    private string _databasePath = @"%APPDATA%\RobloxPianoV2\library_v2.db";

    [RelayCommand]
    private void SelectSection(string section)
    {
        SelectedSection = section;
    }
}
