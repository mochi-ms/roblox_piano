using Microsoft.Data.Sqlite;

namespace RobloxPiano.Infrastructure.Data;

public class SqliteSchemaInitializer
{
    private readonly SqliteConnectionFactory _factory;
    public bool IsFts5Supported { get; private set; } = true;

    public SqliteSchemaInitializer(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(_factory.DatabasePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await using var conn = await _factory.OpenConnectionAsync(readOnly: false, ct);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

        try
        {
            // 1. Base Tables
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    CREATE TABLE IF NOT EXISTS folders (
                        id TEXT PRIMARY KEY,
                        parent_id TEXT,
                        name TEXT NOT NULL,
                        created_at REAL,
                        updated_at REAL DEFAULT 0.0
                    );

                    CREATE TABLE IF NOT EXISTS scores (
                        id TEXT PRIMARY KEY,
                        title TEXT NOT NULL,
                        source_type TEXT DEFAULT 'FILE',
                        source_url TEXT DEFAULT '',
                        filepath TEXT NOT NULL,
                        original_filename TEXT DEFAULT '',
                        file_extension TEXT DEFAULT '',
                        folder_id TEXT DEFAULT NULL,
                        duration REAL DEFAULT 0.0,
                        bpm REAL DEFAULT 120.0,
                        total_notes INTEGER DEFAULT 0,
                        tags TEXT DEFAULT '',
                        analysis_status TEXT DEFAULT 'READY',
                        analysis_error TEXT DEFAULT '',
                        favorite INTEGER DEFAULT 0,
                        created_at REAL,
                        updated_at REAL DEFAULT 0.0,
                        last_played_at REAL DEFAULT 0.0,
                        FOREIGN KEY(folder_id) REFERENCES folders(id) ON DELETE SET NULL
                    );

                    CREATE INDEX IF NOT EXISTS idx_scores_folder_id ON scores(folder_id);
                    CREATE INDEX IF NOT EXISTS idx_scores_created_at ON scores(created_at);
                    CREATE INDEX IF NOT EXISTS idx_scores_updated_at ON scores(updated_at);
                    CREATE INDEX IF NOT EXISTS idx_scores_favorite ON scores(favorite);
                    CREATE INDEX IF NOT EXISTS idx_folders_parent_id ON folders(parent_id);

                    PRAGMA user_version = 1;
                """;
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // 2. FTS5 Virtual Table & Triggers
            try
            {
                await using var ftsCmd = conn.CreateCommand();
                ftsCmd.Transaction = tx;
                ftsCmd.CommandText = """
                    CREATE VIRTUAL TABLE IF NOT EXISTS scores_fts USING fts5(id UNINDEXED, title, tags, original_filename);

                    CREATE TRIGGER IF NOT EXISTS trg_scores_ai AFTER INSERT ON scores BEGIN
                        INSERT INTO scores_fts (id, title, tags, original_filename) VALUES (new.id, new.title, new.tags, new.original_filename);
                    END;

                    CREATE TRIGGER IF NOT EXISTS trg_scores_au AFTER UPDATE ON scores BEGIN
                        UPDATE scores_fts SET title = new.title, tags = new.tags, original_filename = new.original_filename WHERE id = new.id;
                    END;

                    CREATE TRIGGER IF NOT EXISTS trg_scores_ad AFTER DELETE ON scores BEGIN
                        DELETE FROM scores_fts WHERE id = old.id;
                    END;

                    INSERT INTO scores_fts (id, title, tags, original_filename)
                    SELECT id, title, tags, original_filename FROM scores
                    WHERE id NOT IN (SELECT id FROM scores_fts);
                """;
                await ftsCmd.ExecuteNonQueryAsync(ct);
                IsFts5Supported = true;
            }
            catch
            {
                IsFts5Supported = false;
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
