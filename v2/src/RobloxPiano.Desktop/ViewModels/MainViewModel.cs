using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobloxPiano.Core.Library;
using RobloxPiano.Core.Music;
using RobloxPiano.Core.Piano;
using RobloxPiano.Playback.Windows.WindowsIntegration;

namespace RobloxPiano.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    [ObservableProperty]
    private object _currentView;

    private readonly PianoProfileContext _profileContext;
    private readonly PlayerViewModel _playerViewModel;
    private readonly LibraryViewModel _libraryViewModel;
    private readonly ImportViewModel _importViewModel;
    private readonly TranscribeViewModel _transcribeViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly IGlobalHotkeyService _hotkeyService;
    private readonly EventHandler<ScoreItem> _openScoreHandler;
    private readonly EventHandler _viewLibraryHandler;
    private readonly EventHandler _scoreImportedHandler;
    private readonly EventHandler<HotkeyAction> _hotkeyHandler;
    private readonly EventHandler<MusicTimeline> _transcribeOpenScoreHandler;

    private bool _disposed;

    public PianoProfileContext ProfileContext => _profileContext;
    public PlayerViewModel PlayerViewModel => _playerViewModel;
    public LibraryViewModel LibraryViewModel => _libraryViewModel;
    public ImportViewModel ImportViewModel => _importViewModel;
    public TranscribeViewModel TranscribeViewModel => _transcribeViewModel;
    public SettingsViewModel SettingsViewModel => _settingsViewModel;
    public IGlobalHotkeyService HotkeyService => _hotkeyService;

    public MainViewModel() : this(null, null, null, null, null, null, null)
    {
    }

    public MainViewModel(
        PlayerViewModel? playerViewModel = null,
        LibraryViewModel? libraryViewModel = null,
        TranscribeViewModel? transcribeViewModel = null,
        SettingsViewModel? settingsViewModel = null,
        IGlobalHotkeyService? hotkeyService = null,
        ImportViewModel? importViewModel = null,
        PianoProfileContext? profileContext = null)
    {
        _profileContext = profileContext ?? playerViewModel?.ProfileContext ?? importViewModel?.ProfileContext ?? transcribeViewModel?.ProfileContext ?? new PianoProfileContext();
        _playerViewModel = playerViewModel ?? new PlayerViewModel(profileContext: _profileContext);
        _libraryViewModel = libraryViewModel ?? new LibraryViewModel();
        _importViewModel = importViewModel ?? new ImportViewModel(profileContext: _profileContext);
        _transcribeViewModel = transcribeViewModel ?? new TranscribeViewModel(profileContext: _profileContext);
        _settingsViewModel = settingsViewModel ?? new SettingsViewModel();
        _hotkeyService = hotkeyService ?? new GlobalHotkeyService();

        _openScoreHandler = async (_, score) =>
        {
            try
            {
                await _playerViewModel.LoadScoreAsync(score);
                CurrentView = _playerViewModel;
            }
            catch (ObjectDisposedException)
            {
                // Shutdown race: safely ignore
            }
            catch (Exception ex)
            {
                _playerViewModel.StatusText = $"악보 불러오기 실패: {ex.Message}";
            }
        };

        _viewLibraryHandler = (_, _) =>
        {
            CurrentView = _libraryViewModel;
        };

        _scoreImportedHandler = async (_, _) =>
        {
            try
            {
                await _libraryViewModel.ReloadQueryAsync();
            }
            catch (ObjectDisposedException) { }
            catch { }
        };

        _hotkeyHandler = async (_, action) =>
        {
            try
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
            }
            catch (ObjectDisposedException)
            {
                // Shutdown race: safely ignore
            }
            catch (Exception ex)
            {
                _playerViewModel.StatusText = $"단축키 처리 오류: {ex.Message}";
            }
        };

        _transcribeOpenScoreHandler = (_, timeline) =>
        {
            try
            {
                _playerViewModel.LoadTimeline(timeline, timeline.Title ?? "AI 변환 악보", "AI MIDI");
                CurrentView = _playerViewModel;
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                _playerViewModel.StatusText = $"악보 불러오기 실패: {ex.Message}";
            }
        };

        _libraryViewModel.OpenScoreRequested += _openScoreHandler;
        _importViewModel.OpenScoreRequested += _openScoreHandler;
        _importViewModel.ViewLibraryRequested += _viewLibraryHandler;
        _importViewModel.ScoreImported += _scoreImportedHandler;
        _transcribeViewModel.OpenScoreRequested += _transcribeOpenScoreHandler;
        _transcribeViewModel.ScoreImported += _scoreImportedHandler;
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
            "Import" or "가져오기" or "악보 가져오기" => _importViewModel,
            "Transcribe" or "오디오 변환" => _transcribeViewModel,
            "Settings" or "설정" => _settingsViewModel,
            _ => _playerViewModel
        };
    }

    [RelayCommand]
    private void ToggleOverlay()
    {
        _playerViewModel.OverlayViewModel.IsVisible = !_playerViewModel.OverlayViewModel.IsVisible;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _libraryViewModel.OpenScoreRequested -= _openScoreHandler;
        _importViewModel.OpenScoreRequested -= _openScoreHandler;
        _importViewModel.ViewLibraryRequested -= _viewLibraryHandler;
        _importViewModel.ScoreImported -= _scoreImportedHandler;
        _transcribeViewModel.OpenScoreRequested -= _transcribeOpenScoreHandler;
        _transcribeViewModel.ScoreImported -= _scoreImportedHandler;
        _hotkeyService.HotkeyPressed -= _hotkeyHandler;
        _hotkeyService.Dispose();
        _importViewModel.Dispose();
        _transcribeViewModel.Dispose();
        _playerViewModel.Dispose();
    }
}
