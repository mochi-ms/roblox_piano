namespace RobloxPiano.Core.Library;

public class ScoreItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string SourceType { get; set; } = "FILE";
    public string SourceUrl { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string OriginalFilename { get; set; } = string.Empty;
    public string FileExtension { get; set; } = string.Empty;
    public string? FolderId { get; set; }
    public double Duration { get; set; } = 0.0;
    public double Bpm { get; set; } = 120.0;
    public int TotalNotes { get; set; } = 0;
    public string Tags { get; set; } = string.Empty;
    public string AnalysisStatus { get; set; } = "READY";
    public string AnalysisError { get; set; } = string.Empty;
    public bool Favorite { get; set; } = false;
    public double CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
    public double UpdatedAt { get; set; } = 0.0;
    public double LastPlayedAt { get; set; } = 0.0;

    public ScoreItem() { }

    public ScoreItem(
        string id,
        string title,
        string sourceType,
        string sourceUrl,
        string filePath,
        string originalFilename = "",
        string fileExtension = "",
        string? folderId = null,
        double duration = 0.0,
        double bpm = 120.0,
        int totalNotes = 0,
        string tags = "",
        string analysisStatus = "READY",
        string analysisError = "",
        bool favorite = false,
        double? createdAt = null,
        double updatedAt = 0.0,
        double lastPlayedAt = 0.0)
    {
        Id = id;
        Title = title;
        SourceType = sourceType;
        SourceUrl = sourceUrl;
        FilePath = filePath;
        OriginalFilename = originalFilename;
        FileExtension = fileExtension;
        FolderId = folderId;
        Duration = duration;
        Bpm = bpm;
        TotalNotes = totalNotes;
        Tags = tags;
        AnalysisStatus = analysisStatus;
        AnalysisError = analysisError;
        Favorite = favorite;
        CreatedAt = createdAt ?? (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0);
        UpdatedAt = updatedAt;
        LastPlayedAt = lastPlayedAt;
    }

    public List<string> GetTagsList()
    {
        if (string.IsNullOrWhiteSpace(Tags))
            return new List<string>();

        return Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    public void SetTagsList(IEnumerable<string> tags)
    {
        Tags = string.Join(",", tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()));
    }
}
