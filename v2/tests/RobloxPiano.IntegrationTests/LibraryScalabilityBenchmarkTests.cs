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
    public async Task Benchmark_FullScalabilitySuite(int count)
    {
        await _repository.InitializeAsync();

        // 1. Bulk Insert
        var scores = new List<ScoreItem>(count);
        for (int i = 1; i <= count; i++)
        {
            scores.Add(new ScoreItem(
                id: $"score-{i:D6}",
                title: $"Synthetic Piano Track {i:D6}",
                sourceType: i % 2 == 0 ? "MIDI" : "MML",
                sourceUrl: "",
                filePath: $"C:\\music\\track_{i:D6}.mid",
                originalFilename: $"track_{i:D6}.mid",
                fileExtension: ".mid",
                duration: 120.0 + (i % 300),
                bpm: 120.0 + (i % 60),
                totalNotes: 200 + (i % 1000),
                tags: i % 10 == 0 ? "benchmark,favorite,special" : "benchmark",
                favorite: i % 10 == 0,
                createdAt: 1700000000.0 + i
            ));
        }

        var sw = Stopwatch.StartNew();
        await _repository.BulkImportAsync(Array.Empty<FolderItem>(), scores);
        sw.Stop();
        var insertMs = sw.ElapsedMilliseconds;

        // 2. First page query (100 items)
        sw.Restart();
        var firstPage = await _repository.QueryScoresAsync(new LibraryQuery
        {
            PageIndex = 0,
            PageSize = 100,
            SortBy = LibrarySortColumn.Title
        });
        sw.Stop();
        var firstPageMs = sw.ElapsedMilliseconds;
        Assert.Equal(count, firstPage.TotalCount);
        Assert.Equal(100, firstPage.Items.Count);

        // 3. Next page query (page 1)
        sw.Restart();
        var nextPage = await _repository.QueryScoresAsync(new LibraryQuery
        {
            PageIndex = 1,
            PageSize = 100,
            SortBy = LibrarySortColumn.Title
        });
        sw.Stop();
        var nextPageMs = sw.ElapsedMilliseconds;
        Assert.Equal(100, nextPage.Items.Count);

        // 4. Total count query
        sw.Restart();
        var totalCount = await _repository.GetScoreCountAsync();
        sw.Stop();
        var countMs = sw.ElapsedMilliseconds;
        Assert.Equal(count, totalCount);

        // 5. Search query
        sw.Restart();
        var searchResult = await _repository.QueryScoresAsync(new LibraryQuery
        {
            SearchKeyword = "Track 000500",
            PageSize = 50
        });
        sw.Stop();
        var searchMs = sw.ElapsedMilliseconds;
        Assert.True(searchResult.TotalCount >= 1);

        // 6. Sort query (by Duration DESC)
        sw.Restart();
        var sortResult = await _repository.QueryScoresAsync(new LibraryQuery
        {
            PageIndex = 0,
            PageSize = 100,
            SortBy = LibrarySortColumn.Duration,
            SortDescending = true
        });
        sw.Stop();
        var sortMs = sw.ElapsedMilliseconds;
        Assert.Equal(100, sortResult.Items.Count);

        // 7. Favorite filter query
        sw.Restart();
        var favResult = await _repository.QueryScoresAsync(new LibraryQuery
        {
            FavoritesOnly = true,
            PageSize = 100
        });
        sw.Stop();
        var favMs = sw.ElapsedMilliseconds;
        Assert.Equal(count / 10, favResult.TotalCount);

        // 8. Single row update latency
        var targetScore = firstPage.Items[0];
        targetScore.Title = "Updated Benchmark Title";
        sw.Restart();
        await _repository.UpdateScoreAsync(targetScore);
        sw.Stop();
        var updateMs = sw.ElapsedMilliseconds;

        _output.WriteLine($"=================================================================");
        _output.WriteLine($"[Benchmark {count:N0} Scores Report]");
        _output.WriteLine($"  - Bulk Insert: {insertMs} ms");
        _output.WriteLine($"  - First Page (100 items): {firstPageMs} ms");
        _output.WriteLine($"  - Next Page (Page 1): {nextPageMs} ms");
        _output.WriteLine($"  - Total Count: {countMs} ms");
        _output.WriteLine($"  - Keyword Search: {searchMs} ms");
        _output.WriteLine($"  - Sort (Duration DESC): {sortMs} ms");
        _output.WriteLine($"  - Favorites Filter: {favMs} ms");
        _output.WriteLine($"  - Single Row Update: {updateMs} ms");
        _output.WriteLine($"=================================================================");
    }
}
