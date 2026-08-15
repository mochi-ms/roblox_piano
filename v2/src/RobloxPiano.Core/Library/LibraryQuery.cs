namespace RobloxPiano.Core.Library;

public enum LibrarySortColumn
{
    Title,
    FileExtension,
    Duration,
    Bpm,
    TotalNotes,
    UpdatedAt,
    CreatedAt
}

public class LibraryQuery
{
    public string? FolderId { get; set; }
    public string? SearchKeyword { get; set; }
    public bool FavoritesOnly { get; set; }
    public LibrarySortColumn SortBy { get; set; } = LibrarySortColumn.Title;
    public bool SortDescending { get; set; } = false;
    public int PageIndex { get; set; } = 0;
    public int PageSize { get; set; } = 100;
}

public class LibraryPage
{
    public IReadOnlyList<ScoreItem> Items { get; set; } = Array.Empty<ScoreItem>();
    public int TotalCount { get; set; }
    public int PageIndex { get; set; }
    public int PageSize { get; set; }

    public LibraryPage() { }

    public LibraryPage(IReadOnlyList<ScoreItem> items, int totalCount, int pageIndex, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        PageIndex = pageIndex;
        PageSize = pageSize;
    }
}

public class LibraryImportSummary
{
    public string? RootFolderId { get; set; }
    public string RootFolderName { get; set; } = string.Empty;
    public int TotalScanned { get; set; }
    public int ImportedFolders { get; set; }
    public int ImportedScores { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public List<(string Path, string Reason)> FailedItems { get; set; } = new();
    public bool Cancelled { get; set; }
}
