using Microsoft.Data.Sqlite;
using RobloxPiano.Core.Library;

namespace RobloxPiano.Infrastructure.Data;

public class V1ReadOnlyLibraryReader
{
    private readonly string _v1DatabasePath;

    public V1ReadOnlyLibraryReader(string v1DatabasePath)
    {
        _v1DatabasePath = v1DatabasePath;
    }

    public string V1DatabasePath => _v1DatabasePath;

    public async Task<(IReadOnlyList<FolderItem> Folders, IReadOnlyList<ScoreItem> Scores)> ReadAllAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_v1DatabasePath))
        {
            return (Array.Empty<FolderItem>(), Array.Empty<ScoreItem>());
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _v1DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            ForeignKeys = false
        };

        await using var conn = new SqliteConnection(builder.ToString());
        await conn.OpenAsync(ct);

        // 1. Read Folders (if table exists)
        var folders = new List<FolderItem>();
        bool hasFoldersTable = false;

        await using (var checkCmd = conn.CreateCommand())
        {
            checkCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='folders';";
            var result = await checkCmd.ExecuteScalarAsync(ct);
            hasFoldersTable = result != null;
        }

        if (hasFoldersTable)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM folders;";
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            var colNames = GetColumnNames(reader);
            while (await reader.ReadAsync(ct))
            {
                string id = reader.GetString(reader.GetOrdinal("id"));
                string? parentId = colNames.Contains("parent_id") && !reader.IsDBNull(reader.GetOrdinal("parent_id"))
                    ? reader.GetString(reader.GetOrdinal("parent_id")) : null;
                string name = colNames.Contains("name") ? reader.GetString(reader.GetOrdinal("name")) : "Folder";
                double createdAt = colNames.Contains("created_at") && !reader.IsDBNull(reader.GetOrdinal("created_at"))
                    ? reader.GetDouble(reader.GetOrdinal("created_at")) : 0.0;
                double updatedAt = colNames.Contains("updated_at") && !reader.IsDBNull(reader.GetOrdinal("updated_at"))
                    ? reader.GetDouble(reader.GetOrdinal("updated_at")) : 0.0;

                folders.Add(new FolderItem(id, parentId, name, createdAt, updatedAt));
            }
        }

        // 2. Read Scores (if table exists)
        var scores = new List<ScoreItem>();
        bool hasScoresTable = false;

        await using (var checkCmd = conn.CreateCommand())
        {
            checkCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='scores';";
            var result = await checkCmd.ExecuteScalarAsync(ct);
            hasScoresTable = result != null;
        }

        if (hasScoresTable)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM scores;";
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            var colNames = GetColumnNames(reader);
            while (await reader.ReadAsync(ct))
            {
                string id = reader.GetString(reader.GetOrdinal("id"));
                string title = colNames.Contains("title") ? reader.GetString(reader.GetOrdinal("title")) : "Untitled";
                string sourceType = colNames.Contains("source_type") && !reader.IsDBNull(reader.GetOrdinal("source_type"))
                    ? reader.GetString(reader.GetOrdinal("source_type")) : "FILE";
                string sourceUrl = colNames.Contains("source_url") && !reader.IsDBNull(reader.GetOrdinal("source_url"))
                    ? reader.GetString(reader.GetOrdinal("source_url")) : "";
                string filepath = colNames.Contains("filepath") ? reader.GetString(reader.GetOrdinal("filepath")) : "";
                string originalFilename = colNames.Contains("original_filename") && !reader.IsDBNull(reader.GetOrdinal("original_filename"))
                    ? reader.GetString(reader.GetOrdinal("original_filename")) : Path.GetFileName(filepath);
                string fileExtension = colNames.Contains("file_extension") && !reader.IsDBNull(reader.GetOrdinal("file_extension"))
                    ? reader.GetString(reader.GetOrdinal("file_extension")) : Path.GetExtension(filepath).ToLowerInvariant();
                string? folderId = colNames.Contains("folder_id") && !reader.IsDBNull(reader.GetOrdinal("folder_id"))
                    ? reader.GetString(reader.GetOrdinal("folder_id")) : null;
                double duration = colNames.Contains("duration") && !reader.IsDBNull(reader.GetOrdinal("duration"))
                    ? reader.GetDouble(reader.GetOrdinal("duration")) : 0.0;
                double bpm = colNames.Contains("bpm") && !reader.IsDBNull(reader.GetOrdinal("bpm"))
                    ? reader.GetDouble(reader.GetOrdinal("bpm")) : 120.0;
                int totalNotes = colNames.Contains("total_notes") && !reader.IsDBNull(reader.GetOrdinal("total_notes"))
                    ? reader.GetInt32(reader.GetOrdinal("total_notes")) : 0;
                string tags = colNames.Contains("tags") && !reader.IsDBNull(reader.GetOrdinal("tags"))
                    ? reader.GetString(reader.GetOrdinal("tags")) : "";
                string analysisStatus = colNames.Contains("analysis_status") && !reader.IsDBNull(reader.GetOrdinal("analysis_status"))
                    ? reader.GetString(reader.GetOrdinal("analysis_status")) : "READY";
                string analysisError = colNames.Contains("analysis_error") && !reader.IsDBNull(reader.GetOrdinal("analysis_error"))
                    ? reader.GetString(reader.GetOrdinal("analysis_error")) : "";
                bool favorite = colNames.Contains("favorite") && !reader.IsDBNull(reader.GetOrdinal("favorite"))
                    && reader.GetInt32(reader.GetOrdinal("favorite")) != 0;
                double createdAt = colNames.Contains("created_at") && !reader.IsDBNull(reader.GetOrdinal("created_at"))
                    ? reader.GetDouble(reader.GetOrdinal("created_at")) : 0.0;
                double updatedAt = colNames.Contains("updated_at") && !reader.IsDBNull(reader.GetOrdinal("updated_at"))
                    ? reader.GetDouble(reader.GetOrdinal("updated_at")) : 0.0;
                double lastPlayedAt = colNames.Contains("last_played_at") && !reader.IsDBNull(reader.GetOrdinal("last_played_at"))
                    ? reader.GetDouble(reader.GetOrdinal("last_played_at")) : 0.0;

                scores.Add(new ScoreItem(
                    id: id,
                    title: title,
                    sourceType: sourceType,
                    sourceUrl: sourceUrl,
                    filePath: filepath,
                    originalFilename: originalFilename,
                    fileExtension: fileExtension,
                    folderId: folderId,
                    duration: duration,
                    bpm: bpm,
                    totalNotes: totalNotes,
                    tags: tags,
                    analysisStatus: analysisStatus,
                    analysisError: analysisError,
                    favorite: favorite,
                    createdAt: createdAt,
                    updatedAt: updatedAt,
                    lastPlayedAt: lastPlayedAt
                ));
            }
        }

        return (folders, scores);
    }

    private static HashSet<string> GetColumnNames(SqliteDataReader reader)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < reader.FieldCount; i++)
        {
            set.Add(reader.GetName(i));
        }
        return set;
    }
}
