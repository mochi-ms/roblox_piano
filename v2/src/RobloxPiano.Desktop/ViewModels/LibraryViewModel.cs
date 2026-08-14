using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RobloxPiano.Desktop.ViewModels;

public class MockScoreItem
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Duration { get; set; } = "";
    public string Bpm { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Modified { get; set; } = "";
    public string Icon { get; set; } = "";
}

public partial class LibraryViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<MockScoreItem> _mockScores = new();

    public LibraryViewModel()
    {
        MockScores.Add(new MockScoreItem { Name = "Mrs. GREEN APPLE - ライラック", Type = "MIDI", Duration = "04:46", Bpm = "150", Notes = "1,482", Modified = "2024-05-12", Icon = "M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" });
        MockScores.Add(new MockScoreItem { Name = "あいみょん - マリーゴールド", Type = "MML", Duration = "05:06", Bpm = "106", Notes = "924", Modified = "2024-03-21", Icon = "M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm2 16H8V4h5v5h5v9z" });
        MockScores.Add(new MockScoreItem { Name = "Practice 01 (C Major Scale)", Type = "MusicXML", Duration = "01:20", Bpm = "120", Notes = "15", Modified = "2024-01-10", Icon = "M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm2 16H8V4h5v5h5v9z" });
        for (int i = 2; i <= 15; i++)
        {
            MockScores.Add(new MockScoreItem { Name = $"Practice {i:00}", Type = "MIDI", Duration = "02:00", Bpm = "120", Notes = "300", Modified = "2024-01-10", Icon = "M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" });
        }
    }
}
