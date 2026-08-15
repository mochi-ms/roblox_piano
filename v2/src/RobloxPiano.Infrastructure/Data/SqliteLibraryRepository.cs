using Microsoft.Data.Sqlite;
using RobloxPiano.Core.Library;

namespace RobloxPiano.Infrastructure.Data;

public class SqliteLibraryRepository : ILibraryRepository
{
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteSchemaInitializer _initializer;

    public SqliteLibraryRepository(SqliteConnectionFactory factory)
    {
        _factory = factory;
        _initializer = new SqliteSchemaInitializer(_factory);
    }

    public SqliteLibraryRepository(string? databasePath = null)
        : this(new SqliteConnectionFactory(databasePath))
    {
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _initializer.InitializeAsync(ct);
    }

    public async Task InsertScoreAsync(ScoreItem score, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(readOnly: false, ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO scores 
            (id, title, source_type, source_url, filepath, original_filename, file_extension, folder_id,
             duration, bpm, total_notes, tags, analysis_status, analysis_error, favorite,
             created_at, updated_at, last_played_at)
            VALUES (@id, @title, @source_type, @source_url, @filepath, @original_filename, @file_extension, @folder_id,
                    @duration, @bpm, @total_notes, @tags, @analysis_status, @analysis_error, @favorite,
                    @created_at, @updated_at, @last_played_at);
        """;

        cmd.Parameters.AddWithValue("@id", score.Id);
        cmd.Parameters.AddWithValue("@title", score.Title);
        cmd.Parameters.AddWithValue("@source_type", score.SourceType);
        cmd.Parameters.AddWithValue("@source_url", score.SourceUrl);
        cmd.Parameters.AddWithValue("@filepath", score.FilePath);
        cmd.Parameters.AddWithValue("@original_filename", score.OriginalFilename);
        cmd.Parameters.AddWithValue("@file_extension", score.FileExtension);
        cmd.Parameters.AddWithValue("@folder_id", (object?)score.FolderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@duration", score.Duration);
        cmd.Parameters.AddWithValue("@bpm", score.Bpm);
        cmd.Parameters.AddWithValue("@total_notes", score.TotalNotes);
        cmd.Parameters.AddWithValue("@tags", score.Tags);
        cmd.Parameters.AddWithValue("@analysis_status", score.AnalysisStatus);
        cmd.Parameters.AddWithValue("@analysis_error", score.AnalysisError);
        cmd.Parameters.AddWithValue("@favorite", score.Favorite ? 1 : 0);
        cmd.Parameters.AddWithValue("@created_at", score.CreatedAt);
        cmd.Parameters.AddWithValue("@updated_at", score.UpdatedAt);
        cmd.Parameters.AddWithValue("@last_played_at", score.LastPlayedAt);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateScoreAsync(ScoreItem score, CancellationToken ct = default)
    {
        await InsertScoreAsync(score, ct);
    }

    public async Task DeleteScoreAsync(string scoreId, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(readOnly: false, ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM scores WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", scoreId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<ScoreItem?> GetScoreAsync(string scoreId, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(readOnly: true, ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM scores WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", scoreId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return ReadScore(reader);
        }
        return null;
    }

    public async Task<IReadOnlyList<ScoreItem>> GetAllScoresAsync(CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(readOnly: true, ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM scores ORDER BY created_at DESC;";

        var list = new List<ScoreItem>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(ReadScore(reader));
        }
        return list;
    }

    public async Task<LibraryPage> QueryScoresAsync(LibraryQuery query, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(readOnly: true, ct);

        var whereClauses = new List<string>();
        var parameters = new List<SqliteParameter>();

        bool isSearching = !string.IsNullOrWhiteSpace(query.SearchKeyword);
        if (isSearching)
        {
            var keyword = query.SearchKeyword!.Trim();
            // Try FTS5 match query if enabled, otherwise LIKE fallback
            if (_initializer.IsFts5Supported && !keyword.Contains('"') && !keyword.Contains('*'))
            {
                whereClauses.Add("s.id IN (SELECT id FROM scores_fts WHERE scores_fts MATCH @ftsKw)");
                parameters.Add(new SqliteParameter("@ftsKw", $"{keyword}*"));
            }
            else
            {
                whereClauses.Add("(s.title LIKE @likeKw OR s.tags LIKE @likeKw OR s.original_filename LIKE @likeKw)");
                parameters.Add(new SqliteParameter("@likeKw", $"%{keyword}%"));
            }
        }
        else
        {
            if (query.FavoritesOnly)
            {
                whereClauses.Add("s.favorite = 1");
            }
            else if (!string.IsNullOrEmpty(query.FolderId))
            {
                whereClauses.Add("s.folder_id = @folderId");
                parameters.Add(new SqliteParameter("@folderId", query.FolderId));
            }
            else
            {
                whereClauses.Add("s.folder_id IS NULL");
            }
        }

        string whereSql = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

        // 1. Total Count Query
        int totalCount = 0;
        await using (var countCmd = conn.CreateCommand())
        {
            countCmd.CommandText = $"SELECT COUNT(*) FROM scores s {whereSql};";
            foreach (var p in parameters)
            {
                countCmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
            }
            var countResult = await countCmd.ExecuteScalarAsync(ct);
            totalCount = Convert.ToInt32(countResult);
        }

        // 2. Sort column mapping
        string sortCol = query.SortBy switch
        {
            LibrarySortColumn.Title => "s.title COLLATE NOCASE",
            LibrarySortColumn.FileExtension => "s.file_extension",
            LibrarySortColumn.Duration => "s.duration",
            LibrarySortColumn.Bpm => "s.bpm",
            LibrarySortColumn.TotalNotes => "s.total_notes",
            LibrarySortColumn.UpdatedAt => "s.updated_at",
            LibrarySortColumn.CreatedAt => "s.created_at",
            _ => "s.title COLLATE NOCASE"
        };
        string sortDir = query.SortDescending ? "DESC" : "ASC";

        // 3. Paged Select Query
        int offset = Math.Max(0, query.PageIndex) * Math.Max(1, query.PageSize);
        var items = new List<ScoreItem>();

        await using (var selectCmd = conn.CreateCommand())
        {
            selectCmd.CommandText = $"""
                SELECT s.* FROM scores s 
                {whereSql} 
                ORDER BY {sortCol} {sortDir} 
                LIMIT @limit OFFSET @offset;
            """;

            foreach (var p in parameters)
            {
                selectCmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
            }
            selectCmd.Parameters.AddWithValue("@limit", query.PageSize);
            selectCmd.Parameters.AddWithValue("@offset", offset);

            await using var reader = await selectCmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                items.Add(ReadScore(reader));
            }
        }

        return new LibraryPage(items, totalCount, query.PageIndex, query.PageSize);
    }

    public async Task<int> GetScoreCountAsync(string? folderId = null, bool favoritesOnly = false, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(readOnly: true, ct);
        await using var cmd = conn.CreateCommand();

        if (favoritesOnly)
        {
            cmd.CommandText = "SELECT COUNT(*) FROM scores WHERE favorite = 1;";
        }
        else if (folderId != null)
        {
            cmd.CommandText = "SELECT COUNT(*) FROM scores WHERE folder_id = @folderId;";
            cmd.Parameters.AddWithValue("@folderId", folderId);
        }
        else
        {
            cmd.CommandText = "SELECT COUNT(*) FROM scores WHERE folder_id IS NULL;";
        }

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task ToggleFavoriteAsync(string scoreId, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(readOnly: false, ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE scores SET favorite = CASE WHEN favorite = 1 THEN 0 ELSE 1 END WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", scoreId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateLastPlayedAsync(string scoreId, double timestamp, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(readOnly: false, ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE scores SET last_played_at = @ts WHERE id = @id;";
        cmd.Parameters.AddWithValue("@ts", timestamp);
        cmd.Parameters.AddWithValue("@id", scoreId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task InsertFolderAsync(FolderItem folder, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(readOnly: false, ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO folders 
            (id, parent_id, name, created_at, updated_at)
            VALUES (@id, @parent_id, @name, @created_at, @updated_at);
        """;

        cmd.Parameters.AddWithValue("@id", folder.Id);
        cmd.Parameters.AddWithValue("@parent_id", (object?)folder.ParentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@name", folder.Name);
        cmd.Parameters.AddWithValue("@created_at", folder.CreatedAt);
        cmd.Parameters.AddWithValue("@updated_at", folder.UpdatedAt);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateFolderAsync(FolderItem folder, CancellationToken ct = default)
    {
        await InsertFolderAsync(folder, ct);
    }

    public async Task DeleteFolderAsync(string folderId, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(readOnly: false, ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM folders WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", folderId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<FolderItem?> GetFolderAsync(string folderId, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(readOnly: true, ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM folders WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", folderId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return ReadFolder(reader);
        }
        return null;
    }

    public async Task<IReadOnlyList<FolderItem>> GetAllFoldersAsync(CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(readOnly: true, ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM folders ORDER BY name COLLATE NOCASE ASC;";

        var list = new List<FolderItem>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(ReadFolder(reader));
        }
        return list;
    }

    public async Task<IReadOnlyList<FolderItem>> GetChildFoldersAsync(string? parentId, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(readOnly: true, ct);
        await using var cmd = conn.CreateCommand();

        if (parentId == null)
        {
            cmd.CommandText = "SELECT * FROM folders WHERE parent_id IS NULL ORDER BY name COLLATE NOCASE ASC;";
        }
        else
        {
            cmd.CommandText = "SELECT * FROM folders WHERE parent_id = @parentId ORDER BY name COLLATE NOCASE ASC;";
            cmd.Parameters.AddWithValue("@parentId", parentId);
        }

        var list = new List<FolderItem>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(ReadFolder(reader));
        }
        return list;
    }

    private static ScoreItem ReadScore(SqliteDataReader r)
    {
        return new ScoreItem(
            id: r.GetString(r.GetOrdinal("id")),
            title: r.GetString(r.GetOrdinal("title")),
            sourceType: r.IsDBNull(r.GetOrdinal("source_type")) ? "FILE" : r.GetString(r.GetOrdinal("source_type")),
            sourceUrl: r.IsDBNull(r.GetOrdinal("source_url")) ? "" : r.GetString(r.GetOrdinal("source_url")),
            filePath: r.GetString(r.GetOrdinal("filepath")),
            originalFilename: r.IsDBNull(r.GetOrdinal("original_filename")) ? "" : r.GetString(r.GetOrdinal("original_filename")),
            fileExtension: r.IsDBNull(r.GetOrdinal("file_extension")) ? "" : r.GetString(r.GetOrdinal("file_extension")),
            folderId: r.IsDBNull(r.GetOrdinal("folder_id")) ? null : r.GetString(r.GetOrdinal("folder_id")),
            duration: r.IsDBNull(r.GetOrdinal("duration")) ? 0.0 : r.GetDouble(r.GetOrdinal("duration")),
            bpm: r.IsDBNull(r.GetOrdinal("bpm")) ? 120.0 : r.GetDouble(r.GetOrdinal("bpm")),
            totalNotes: r.IsDBNull(r.GetOrdinal("total_notes")) ? 0 : r.GetInt32(r.GetOrdinal("total_notes")),
            tags: r.IsDBNull(r.GetOrdinal("tags")) ? "" : r.GetString(r.GetOrdinal("tags")),
            analysisStatus: r.IsDBNull(r.GetOrdinal("analysis_status")) ? "READY" : r.GetString(r.GetOrdinal("analysis_status")),
            analysisError: r.IsDBNull(r.GetOrdinal("analysis_error")) ? "" : r.GetString(r.GetOrdinal("analysis_error")),
            favorite: !r.IsDBNull(r.GetOrdinal("favorite")) && r.GetInt32(r.GetOrdinal("favorite")) != 0,
            createdAt: r.IsDBNull(r.GetOrdinal("created_at")) ? 0.0 : r.GetDouble(r.GetOrdinal("created_at")),
            updatedAt: r.IsDBNull(r.GetOrdinal("updated_at")) ? 0.0 : r.GetDouble(r.GetOrdinal("updated_at")),
            lastPlayedAt: r.IsDBNull(r.GetOrdinal("last_played_at")) ? 0.0 : r.GetDouble(r.GetOrdinal("last_played_at"))
        );
    }

    private static FolderItem ReadFolder(SqliteDataReader r)
    {
        return new FolderItem(
            id: r.GetString(r.GetOrdinal("id")),
            parentId: r.IsDBNull(r.GetOrdinal("parent_id")) ? null : r.GetString(r.GetOrdinal("parent_id")),
            name: r.GetString(r.GetOrdinal("name")),
            createdAt: r.IsDBNull(r.GetOrdinal("created_at")) ? 0.0 : r.GetDouble(r.GetOrdinal("created_at")),
            updatedAt: r.IsDBNull(r.GetOrdinal("updated_at")) ? 0.0 : r.GetDouble(r.GetOrdinal("updated_at"))
        );
    }
}
