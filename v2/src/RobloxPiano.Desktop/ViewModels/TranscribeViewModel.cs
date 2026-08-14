using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RobloxPiano.Desktop.ViewModels;

public partial class TranscribeViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isYouTubeSource = true;

    [ObservableProperty]
    private bool _isLocalFileSource = false;

    [ObservableProperty]
    private string _youTubeUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";

    [ObservableProperty]
    private string _localFilePath = @"C:\Music\recital_piano_solo.wav";

    [ObservableProperty]
    private int _selectedModeIndex = 0;

    [ObservableProperty]
    private int _progressPercent = 62;

    [ObservableProperty]
    private string _progressStatus = "Analyzing piano notes (Frame 4,120 / 6,540)...";

    [RelayCommand]
    private void SelectSource(string source)
    {
        if (source == "YouTube")
        {
            IsYouTubeSource = true;
            IsLocalFileSource = false;
        }
        else
        {
            IsYouTubeSource = false;
            IsLocalFileSource = true;
        }
    }

    [RelayCommand]
    private void PasteUrl()
    {
        YouTubeUrl = "https://www.youtube.com/watch?v=sample_piano_recital";
    }

    [RelayCommand]
    private void ClearUrl()
    {
        YouTubeUrl = string.Empty;
    }

    [RelayCommand]
    private void BrowseFile()
    {
        LocalFilePath = @"C:\Music\beethoven_moonlight.mp3";
    }
}
