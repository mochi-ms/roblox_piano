namespace RobloxPiano.Desktop.ViewModels;

public class BreadcrumbItemViewModel
{
    public string Name { get; }
    public string? FolderId { get; }
    public bool IsLast { get; }

    public BreadcrumbItemViewModel(string name, string? folderId, bool isLast = false)
    {
        Name = name;
        FolderId = folderId;
        IsLast = isLast;
    }
}
