using System.Diagnostics;
using Microsoft.Data.Sqlite;
using RobloxPiano.Core.Library;
using RobloxPiano.Infrastructure.Data;
using Xunit;
using Xunit.Abstractions;

namespace RobloxPiano.IntegrationTests;

public class LibraryScalabilityBenchmarkTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly SqliteLibraryRepository _repository;

    public LibraryScalabilityBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), $"bench_lib_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "bench.db");
        _repository = new SqliteLibraryRepository(_dbPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { }
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(5000)]
    [InlineData(10000)]
    public async Task Benchmark_ScalabilityAndQueryLatency(int count)
    {
        await _repository.InitializeAsync();

        // 1. Bulk Insert
        var sw = Stopwatch.StartNew();
        var connFactory = new SqliteConnectionFactory(_dbPath);
        await using (var conn = await connFactory.OpenConnectionAsync(readOnly: false))
        await using (var tx = (SqliteTransaction)await conn.BeginTransactionAsync())
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO scores (id, title, source_type, source_url, filepath, original_filename, file_extension, folder_id, duration, bpm, total_notes, tags, analysis_status, favorite, created_at, updated_at, last_played_at)
                VALUES (@id, @title, @source_type, @source_url, @filepath, @original_filename, @file_extension, @folder_id, @duration, @bpm, @total_notes, @tags, @analysis_status, @favorite, @created_at, @updated_at, @last_played_at);
            """;

            var pId = cmd.Parameters.Add("@id", SqliteType.Text);
            var pTitle = cmd.Parameters.Add("@title", SqliteType.Text);
            var pSourceType = cmd.Parameters.Add("@source_type", SqliteType.Text);
            var pSourceUrl = cmd.Parameters.Add("@source_url", SqliteType.Text);
            var pFilePath = cmd.Parameters.Add("@filepath", SqliteType.Text);
            var pOrig = cmd.Parameters.Add("@original_filename", SqliteType.Text);
            var pExt = cmd.Parameters.Add("@file_extension", SqliteType.Text);
            var pFolder = cmd.Parameters.Add("@folder_id", SqliteType.Text);
            var pDur = cmd.Parameters.Add("@duration", SqliteType.Real);
            var pBpm = cmd.Parameters.Add("@bpm", SqliteType.Real);
            var pNotes = cmd.Parameters.Add("@total_notes", SqliteType.Integer);
            var pTags = cmd.Parameters.Add("@tags", SqliteType.Text);
            var pStatus = cmd.Parameters.Add("@analysis_status", SqliteType.Text);
            var pFav = cmd.Parameters.Add("@favorite", SqliteType.Integer);
            var pCreated = cmd.Parameters.Add("@created_at", SqliteType.Real);
            var pUpdated = cmd.Parameters.Add("@updated_at", SqliteType.Real);
            var pLastPlayed = cmd.Parameters.Add("@last_played_at", SqliteType.Real);

            for (int i = 1; i <= count; i++)
            {
                pId.Value = $"score-{i:D6}";
                pTitle.Value = $"Synthetic Piano Track {i:D6}";
                pSourceType.Value = i % 2 == 0 ? "MIDI" : "MML";
                pSourceUrl.Value = "";
                pFilePath.Value = $"C:\\music\\track_{i:D6}.mid";
                pOrig.Value = $"track_{i:D6}.mid";
                pExt.Value = ".mid";
                pFolder.Value = DBNull.Value;
                pDur.Value = 120.0 + (i % 300);
                pBpm.Value = 120.0 + (i % 60);
                pNotes.Value = 200 + (i % 1000);
                pTags.Value = i % 10 == 0 ? "benchmark,favorite,special" : "benchmark";
                pStatus.Value = "READY";
                pFav.Value = i % 10 == 0 ? 1 : 0;
                pCreated.Value = 1700000000.0 + i;
                pUpdated.Value = 0.0;
                pLastPlayed.Value = 0.0;

                await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
        }
        sw.Stop();
        var insertMs = sw.ElapsedMilliseconds;

        // 2. Query page latency (Root page 100 items)
        sw.Restart();
        var page = await _repository.QueryScoresAsync(new LibraryQuery
        {
            PageIndex = 0,
            PageSize = 100,
            SortBy = LibrarySortColumn.Title
        });
        sw.Stop();
        var queryMs = sw.ElapsedMilliseconds;

        Assert.Equal(count, page.TotalCount);
        Assert.Equal(100, page.Items.Count);

        // 3. Search query latency (Keyword match)
        sw.Restart();
        var searchResult = await _repository.QueryScoresAsync(new LibraryQuery
        {
            SearchKeyword = "Track 000500",
            PageSize = 50
        });
        sw.Stop();
        var searchMs = sw.ElapsedMilliseconds;

        Assert.True(searchResult.TotalCount >= 1);

        _output.WriteLine($"[Benchmark {count:N0} Items] Insert: {insertMs}ms | Page Query: {queryMs}ms | Search Query: {searchMs}ms");
    }
}
