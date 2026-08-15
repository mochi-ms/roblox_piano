using CommunityToolkit.Mvvm.ComponentModel;
using RobloxPiano.Core.Library;

namespace RobloxPiano.Desktop.ViewModels;

public partial class ScoreItemViewModel : ObservableObject
{
    private static readonly string MidiIconPath = "M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z";
    private static readonly string MmlIconPath = "M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm2 16H8v-2h8v2zm0-4H8v-2h8v2zm-3-5V3.5L18.5 9H13z";

    [ObservableProperty]
    private ScoreItem _model;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _type;

    [ObservableProperty]
    private string _duration;

    [ObservableProperty]
    private string _bpm;

    [ObservableProperty]
    private string _notes;

    [ObservableProperty]
    private string _modified;

    [ObservableProperty]
    private string _icon;

    [ObservableProperty]
    private bool _favorite;

    public string Id => Model.Id;
    public string? FolderId => Model.FolderId;
    public string FilePath => Model.FilePath;

    public ScoreItemViewModel(ScoreItem model)
    {
        _model = model;
        _title = model.Title;
        _type = FormatType(model.SourceType, model.FileExtension);
        _duration = FormatDuration(model.Duration);
        _bpm = model.Bpm > 0 ? $"{Math.Round(model.Bpm)}" : "120";
        _notes = model.TotalNotes > 0 ? $"{model.TotalNotes:N0}" : "-";
        _modified = FormatTimestamp(model.UpdatedAt > 0 ? model.UpdatedAt : model.CreatedAt);
        _icon = model.SourceType == "MML" || model.FileExtension.Equals(".mml", StringComparison.OrdinalIgnoreCase) ? MmlIconPath : MidiIconPath;
        _favorite = model.Favorite;
    }

    public void UpdateFromModel(ScoreItem model)
    {
        Model = model;
        Title = model.Title;
        Type = FormatType(model.SourceType, model.FileExtension);
        Duration = FormatDuration(model.Duration);
        Bpm = model.Bpm > 0 ? $"{Math.Round(model.Bpm)}" : "120";
        Notes = model.TotalNotes > 0 ? $"{model.TotalNotes:N0}" : "-";
        Modified = FormatTimestamp(model.UpdatedAt > 0 ? model.UpdatedAt : model.CreatedAt);
        Favorite = model.Favorite;
    }

    private static string FormatType(string sourceType, string ext)
    {
        if (sourceType == "MML" || ext.Equals(".mml", StringComparison.OrdinalIgnoreCase))
            return "MML";
        if (sourceType == "MIDI" || ext.Equals(".mid", StringComparison.OrdinalIgnoreCase) || ext.Equals(".midi", StringComparison.OrdinalIgnoreCase))
            return "MIDI";
        return string.IsNullOrEmpty(ext) ? "MIDI" : ext.TrimStart('.').ToUpperInvariant();
    }

    private static string FormatDuration(double seconds)
    {
        if (seconds <= 0) return "00:00";
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
    }

    private static string FormatTimestamp(double unixSecs)
    {
        if (unixSecs <= 0) return DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        try
        {
            var dto = DateTimeOffset.FromUnixTimeMilliseconds((long)(unixSecs * 1000.0)).ToLocalTime();
            return dto.ToString("yyyy-MM-dd HH:mm");
        }
        catch
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        }
    }
}
