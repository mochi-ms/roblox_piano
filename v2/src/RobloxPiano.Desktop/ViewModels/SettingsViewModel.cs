using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobloxPiano.Desktop.Services;
using RobloxPiano.Infrastructure.Data;

namespace RobloxPiano.Desktop.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IUserInteractionService _interactionService;

    [ObservableProperty]
    private string _selectedSection = "일반";

    // General (일반)
    [ObservableProperty]
    private bool _openLastWorkspace = true;

    [ObservableProperty]
    private bool _checkForUpdates = true;

    [ObservableProperty]
    private int _defaultStartupPageIndex = 0;

    // Playback (재생)
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

    // Hotkeys (단축키)
    [ObservableProperty]
    private string _hotkeyPlayPause = "F6";

    [ObservableProperty]
    private string _hotkeyStop = "Esc";

    [ObservableProperty]
    private string _hotkeyOverlay = "F4";

    [ObservableProperty]
    private string _hotkeyPanic = "F8";

    // Appearance (화면)
    [ObservableProperty]
    private int _themeIndex = 0;

    [ObservableProperty]
    private int _accentIndex = 0;

    [ObservableProperty]
    private int _uiScaleIndex = 0;

    // Advanced (고급)
    [ObservableProperty]
    private int _logLevelIndex = 0;

    [ObservableProperty]
    private string _databasePath;

    [ObservableProperty]
    private string _buildIdentityText;

    [ObservableProperty]
    private string _versionText;

    public SettingsViewModel() : this(null)
    {
    }

    public SettingsViewModel(IUserInteractionService? interactionService)
    {
        _interactionService = interactionService ?? new WpfUserInteractionService();
        _databasePath = LibraryDatabasePathProvider.GetDefaultDatabasePath();
        _buildIdentityText = BuildIdentity.FullIdentity;
        _versionText = $"버전 {BuildIdentity.Version}";
    }

    [RelayCommand]
    private void SelectSection(string section)
    {
        SelectedSection = section switch
        {
            "General" or "일반" => "일반",
            "Playback" or "재생" => "재생",
            "Roblox" => "Roblox",
            "Hotkeys" or "단축키" => "단축키",
            "Appearance" or "화면" => "화면",
            "Advanced" or "고급" => "고급",
            "About" or "정보" => "정보",
            _ => "일반"
        };
    }

    [RelayCommand]
    public void OpenDatabaseFolder()
    {
        try
        {
            string? dir = Path.GetDirectoryName(DatabasePath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
            else if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            _interactionService.ShowError("폴더 열기 실패", ex.Message);
        }
    }

    [RelayCommand]
    public void ResetSettings()
    {
        if (!_interactionService.Confirm("설정 초기화", "모든 설정을 기본값으로 초기화하시겠습니까?"))
            return;

        OpenLastWorkspace = true;
        CheckForUpdates = true;
        DefaultStartupPageIndex = 0;
        DefaultSpeedIndex = 2;
        DefaultTranspose = "0";
        SustainBehaviorIndex = 0;
        HumanizeJitterMs = "5";
        PianoLayoutIndex = 0;
        TargetModeIndex = 0;
        StopOnFocusLost = true;
        InputMethodIndex = 0;
        HotkeyPlayPause = "F6";
        HotkeyStop = "Esc";
        HotkeyOverlay = "F4";
        HotkeyPanic = "F8";
        ThemeIndex = 0;
        AccentIndex = 0;
        UiScaleIndex = 0;
        LogLevelIndex = 0;

        _interactionService.ShowInfo("설정 초기화", "모든 설정이 기본값으로 초기화되었습니다.");
    }
}
