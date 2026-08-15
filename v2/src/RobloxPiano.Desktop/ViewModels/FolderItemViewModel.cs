using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using RobloxPiano.Core.Library;

namespace RobloxPiano.Desktop.ViewModels;

public partial class FolderItemViewModel : ObservableObject
{
    private static readonly string FolderIconPath = "M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z";

    [ObservableProperty]
    private FolderItem _model;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isCurrent;

    [ObservableProperty]
    private int _depth;

    [ObservableProperty]
    private Thickness _indentPadding = new(12, 6, 12, 6);

    [ObservableProperty]
    private string _icon = FolderIconPath;

    public string Id => Model.Id;
    public string? ParentId => Model.ParentId;

    public FolderItemViewModel(FolderItem model, int depth = 0)
    {
        _model = model;
        _name = model.Name;
        _depth = depth;
        _indentPadding = new Thickness(12 + (depth * 14), 6, 12, 6);
    }
}
