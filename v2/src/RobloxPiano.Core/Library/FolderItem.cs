namespace RobloxPiano.Core.Library;

public class FolderItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public double CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
    public double UpdatedAt { get; set; } = 0.0;

    public FolderItem() { }

    public FolderItem(
        string id,
        string? parentId,
        string name,
        double? createdAt = null,
        double updatedAt = 0.0)
    {
        Id = id;
        ParentId = parentId;
        Name = name;
        CreatedAt = createdAt ?? (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0);
        UpdatedAt = updatedAt;
    }
}
