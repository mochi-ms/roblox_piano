namespace RobloxPiano.Core.Library;

public interface ILibraryRepository
{
    Task InitializeAsync(CancellationToken ct = default);
    Task InsertScoreAsync(ScoreItem score, CancellationToken ct = default);
    Task UpdateScoreAsync(ScoreItem score, CancellationToken ct = default);
    Task DeleteScoreAsync(string scoreId, CancellationToken ct = default);
    Task<ScoreItem?> GetScoreAsync(string scoreId, CancellationToken ct = default);
    Task<IReadOnlyList<ScoreItem>> GetAllScoresAsync(CancellationToken ct = default);
    Task<LibraryPage> QueryScoresAsync(LibraryQuery query, CancellationToken ct = default);
    Task<int> GetScoreCountAsync(string? folderId = null, bool favoritesOnly = false, CancellationToken ct = default);
    Task ToggleFavoriteAsync(string scoreId, CancellationToken ct = default);
    Task UpdateLastPlayedAsync(string scoreId, double timestamp, CancellationToken ct = default);

    Task InsertFolderAsync(FolderItem folder, CancellationToken ct = default);
    Task UpdateFolderAsync(FolderItem folder, CancellationToken ct = default);
    Task DeleteFolderAsync(string folderId, CancellationToken ct = default);
    Task<FolderItem?> GetFolderAsync(string folderId, CancellationToken ct = default);
    Task<IReadOnlyList<FolderItem>> GetAllFoldersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<FolderItem>> GetChildFoldersAsync(string? parentId, CancellationToken ct = default);

    Task BulkImportAsync(IReadOnlyList<FolderItem> folders, IReadOnlyList<ScoreItem> scores, CancellationToken ct = default);
    Task UpdateFolderAndScorePathsAsync(FolderItem folder, IReadOnlyList<ScoreItem> updatedScores, CancellationToken ct = default);
    Task DeleteFolderTreeAsync(IReadOnlyList<string> scoreIds, IReadOnlyList<string> folderIds, CancellationToken ct = default);
}
