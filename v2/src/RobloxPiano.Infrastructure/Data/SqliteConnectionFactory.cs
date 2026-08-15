using Microsoft.Data.Sqlite;

namespace RobloxPiano.Infrastructure.Data;

public class SqliteConnectionFactory
{
    private readonly string _databasePath;

    public SqliteConnectionFactory(string? databasePath = null)
    {
        _databasePath = databasePath ?? LibraryDatabasePathProvider.GetDefaultDatabasePath();
    }

    public string DatabasePath => _databasePath;

    public SqliteConnection CreateConnection(bool readOnly = false)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Cache = SqliteCacheMode.Default
        };

        return new SqliteConnection(builder.ToString());
    }

    public async Task<SqliteConnection> OpenConnectionAsync(bool readOnly = false, CancellationToken ct = default)
    {
        var conn = CreateConnection(readOnly);
        await conn.OpenAsync(ct);

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_keys = ON;";
            await cmd.ExecuteNonQueryAsync(ct);

            if (!readOnly)
            {
                cmd.CommandText = "PRAGMA journal_mode = WAL;";
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }

        return conn;
    }
}
