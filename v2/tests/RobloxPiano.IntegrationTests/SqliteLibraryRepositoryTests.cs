using Microsoft.Data.Sqlite;
using RobloxPiano.Core.Library;
using RobloxPiano.Infrastructure.Data;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class SqliteLibraryRepositoryTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly SqliteLibraryRepository _repository;

    public SqliteLibraryRepositoryTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"test_repo_{Guid.NewGuid():N}.db");
        _repository = new SqliteLibraryRepository(_tempDbPath);
    }

    public void Dispose()
    {
        if (File.Exists(_tempDbPath))
        {
            try { File.Delete(_tempDbPath); } catch { }
        }
    }

    [Fact]
    public async Task InitializeAsync_CreatesTablesAndIndexes()
    {
        await _repository.InitializeAsync();
        var folders = await _repository.GetAllFoldersAsync();
        var scores = await _repository.GetAllScoresAsync();

        Assert.Empty(folders);
        Assert.Empty(scores);
    }

    [Fact]
    public async Task InsertAndGetScore_WorksCorrectly()
    {
        await _repository.InitializeAsync();

        var score = new ScoreItem(
            id: "score-1",
            title: "Test Canon in D",
            sourceType: "MIDI",
            sourceUrl: "C:\\source\\canon.mid",
            filePath: "C:\\library\\canon.mid",
            originalFilename: "canon.mid",
            fileExtension: ".mid",
            duration: 180.5,
            bpm: 110.0,
            totalNotes: 450,
            tags: "classical,canon"
        );

        await _repository.InsertScoreAsync(score);

        var retrieved = await _repository.GetScoreAsync("score-1");
        Assert.NotNull(retrieved);
        Assert.Equal("Test Canon in D", retrieved.Title);
        Assert.Equal("MIDI", retrieved.SourceType);
        Assert.Equal(180.5, retrieved.Duration);
        Assert.Equal(110.0, retrieved.Bpm);
        Assert.Equal(450, retrieved.TotalNotes);
        Assert.Equal("classical,canon", retrieved.Tags);
        Assert.False(retrieved.Favorite);
    }

    [Fact]
    public async Task ToggleFavorite_TogglesState()
    {
        await _repository.InitializeAsync();

        var score = new ScoreItem("score-fav", "Fav Song", "MML", "", "path.mml");
        await _repository.InsertScoreAsync(score);

        await _repository.ToggleFavoriteAsync("score-fav");
        var favScore = await _repository.GetScoreAsync("score-fav");
        Assert.NotNull(favScore);
        Assert.True(favScore.Favorite);

        await _repository.ToggleFavoriteAsync("score-fav");
        var unfavScore = await _repository.GetScoreAsync("score-fav");
        Assert.NotNull(unfavScore);
        Assert.False(unfavScore.Favorite);
    }

    [Fact]
    public async Task QueryScores_PaginationAndSorting_WorkAccurately()
    {
        await _repository.InitializeAsync();

        for (int i = 1; i <= 25; i++)
        {
            var item = new ScoreItem(
                id: $"score-{i:D3}",
                title: $"Song {i:D3}",
                sourceType: "MIDI",
                sourceUrl: "",
                filePath: $"C:\\lib\\song{i}.mid",
                duration: i * 10.0,
                bpm: 100 + i,
                totalNotes: i * 50
            );
            await _repository.InsertScoreAsync(item);
        }

        var page1 = await _repository.QueryScoresAsync(new LibraryQuery
        {
            PageIndex = 0,
            PageSize = 10,
            SortBy = LibrarySortColumn.Title,
            SortDescending = false
        });

        Assert.Equal(25, page1.TotalCount);
        Assert.Equal(10, page1.Items.Count);
        Assert.Equal("Song 001", page1.Items[0].Title);

        var page3 = await _repository.QueryScoresAsync(new LibraryQuery
        {
            PageIndex = 2,
            PageSize = 10,
            SortBy = LibrarySortColumn.Title,
            SortDescending = false
        });

        Assert.Equal(25, page3.TotalCount);
        Assert.Equal(5, page3.Items.Count);
        Assert.Equal("Song 021", page3.Items[0].Title);
    }

    [Fact]
    public async Task SearchScores_MatchesKeywords()
    {
        await _repository.InitializeAsync();

        await _repository.InsertScoreAsync(new ScoreItem("s1", "Beethoven Moonlight Sonata", "MIDI", "", "s1.mid", tags: "piano,sonata"));
        await _repository.InsertScoreAsync(new ScoreItem("s2", "Chopin Nocturne Op9 No2", "MIDI", "", "s2.mid", tags: "romantic"));
        await _repository.InsertScoreAsync(new ScoreItem("s3", "Mozart Turkish March", "MML", "", "s3.mml", tags: "classical"));

        var searchMoonlight = await _repository.QueryScoresAsync(new LibraryQuery { SearchKeyword = "Moonlight" });
        Assert.Single(searchMoonlight.Items);
        Assert.Equal("s1", searchMoonlight.Items[0].Id);

        var searchChopin = await _repository.QueryScoresAsync(new LibraryQuery { SearchKeyword = "Chopin" });
        Assert.Single(searchChopin.Items);
        Assert.Equal("s2", searchChopin.Items[0].Id);
    }

    [Theory]
    [InlineData("\"")]
    [InlineData("*")]
    [InlineData("100%")]
    [InlineData("C++")]
    [InlineData("A-B")]
    [InlineData("(")]
    [InlineData(")")]
    [InlineData("\"quoted string\"")]
    [InlineData("AND OR NOT NEAR")]
    public async Task SearchScores_SpecialCharacters_NeverCrashes(string specialQuery)
    {
        await _repository.InitializeAsync();
        await _repository.InsertScoreAsync(new ScoreItem("s-spec", "Test Track C++ (Special) 100%", "MIDI", "", "s.mid"));

        var result = await _repository.QueryScoresAsync(new LibraryQuery { SearchKeyword = specialQuery });
        Assert.NotNull(result);
    }

    [Fact]
    public async Task Fts5_BackfillAndSync_WorksAccurately()
    {
        // 1. Manually create table and insert rows BEFORE schema initialization
        await using (var conn = new SqliteConnection($"Data Source={_tempDbPath}"))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE scores (
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
                    last_played_at REAL DEFAULT 0.0
                );
                INSERT INTO scores (id, title, filepath, tags) VALUES ('pre-1', 'Preexisting Ghibli Track', 'p1.mid', 'ghibli');
            """;
            await cmd.ExecuteNonQueryAsync();
        }

        // 2. Initialize schema (which must backfill existing scores into scores_fts)
        await _repository.InitializeAsync();

        // Search for preexisting record
        var searchPre = await _repository.QueryScoresAsync(new LibraryQuery { SearchKeyword = "Ghibli" });
        Assert.Single(searchPre.Items);
        Assert.Equal("pre-1", searchPre.Items[0].Id);

        // 3. Test Update sync
        var preScore = await _repository.GetScoreAsync("pre-1");
        Assert.NotNull(preScore);
        preScore.Title = "Updated Makoto Shinkai Track";
        preScore.Tags = "anime,radwimps";
        await _repository.UpdateScoreAsync(preScore);

        var searchUpdated = await _repository.QueryScoresAsync(new LibraryQuery { SearchKeyword = "Radwimps" });
        Assert.Single(searchUpdated.Items);
        Assert.Equal("pre-1", searchUpdated.Items[0].Id);

        // 4. Test Delete sync
        await _repository.DeleteScoreAsync("pre-1");
        var searchDeleted = await _repository.QueryScoresAsync(new LibraryQuery { SearchKeyword = "Radwimps" });
        Assert.Empty(searchDeleted.Items);
    }

    [Fact]
    public async Task FolderCRUD_WorksAccurately()
    {
        await _repository.InitializeAsync();

        var rootFolder = new FolderItem("f-root", null, "Pop");
        await _repository.InsertFolderAsync(rootFolder);

        var subFolder = new FolderItem("f-sub", "f-root", "K-Pop");
        await _repository.InsertFolderAsync(subFolder);

        var all = await _repository.GetAllFoldersAsync();
        Assert.Equal(2, all.Count);

        var children = await _repository.GetChildFoldersAsync("f-root");
        Assert.Single(children);
        Assert.Equal("f-sub", children[0].Id);

        await _repository.DeleteFolderAsync("f-sub");
        var childrenAfter = await _repository.GetChildFoldersAsync("f-root");
        Assert.Empty(childrenAfter);
    }

    [Fact]
    public async Task DeleteFolderTreeAsync_DeletesScoresAndFoldersAtomically()
    {
        await _repository.InitializeAsync();

        var rootFolder = new FolderItem("f-root", null, "Rock");
        var childFolder = new FolderItem("f-child", "f-root", "Classic Rock");
        await _repository.InsertFolderAsync(rootFolder);
        await _repository.InsertFolderAsync(childFolder);

        var scoreRoot = new ScoreItem("s-r", "Rock Anthem", "MIDI", "", "r.mid", folderId: "f-root");
        var scoreChild = new ScoreItem("s-c", "Bohemian", "MIDI", "", "c.mid", folderId: "f-child");
        await _repository.InsertScoreAsync(scoreRoot);
        await _repository.InsertScoreAsync(scoreChild);

        // Execute atomic deletion of subtree
        await _repository.DeleteFolderTreeAsync(
            scoreIds: new[] { "s-c", "s-r" },
            folderIds: new[] { "f-child", "f-root" }
        );

        // Verify all rows removed
        Assert.Null(await _repository.GetFolderAsync("f-root"));
        Assert.Null(await _repository.GetFolderAsync("f-child"));
        Assert.Null(await _repository.GetScoreAsync("s-r"));
        Assert.Null(await _repository.GetScoreAsync("s-c"));
    }
}
