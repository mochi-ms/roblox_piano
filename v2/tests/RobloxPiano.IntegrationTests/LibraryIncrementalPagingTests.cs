using RobloxPiano.Core.Library;
using RobloxPiano.Core.Services;
using RobloxPiano.Desktop.ViewModels;
using RobloxPiano.Infrastructure.Data;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class LibraryIncrementalPagingTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly string _storageRoot;
    private readonly SqliteLibraryRepository _repository;
    private readonly LibraryFileService _fileService;
    private readonly FolderService _folderService;
    private readonly LibraryService _libraryService;

    public LibraryIncrementalPagingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"paging_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _dbPath = Path.Combine(_tempDir, "library.db");
        _storageRoot = Path.Combine(_tempDir, "Library");
        Directory.CreateDirectory(_storageRoot);

        _repository = new SqliteLibraryRepository(_dbPath);
        _fileService = new LibraryFileService(_storageRoot);
        _folderService = new FolderService(_repository, _fileService);
        _libraryService = new LibraryService(_repository, _fileService, _folderService);
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

    private async Task PopulateScoresAsync(int count)
    {
        var scores = new List<ScoreItem>(count);
        for (int i = 1; i <= count; i++)
        {
            scores.Add(new ScoreItem(
                id: $"score-{i:D5}",
                title: $"Track {i:D5}",
                sourceType: "MIDI",
                sourceUrl: "",
                filePath: Path.Combine(_storageRoot, $"track_{i:D5}.mid"),
                originalFilename: $"track_{i:D5}.mid",
                fileExtension: ".mid",
                duration: 120.0,
                bpm: 120.0,
                totalNotes: 300,
                tags: i % 2 == 0 ? "anime,jpop" : "classical",
                favorite: i % 3 == 0,
                createdAt: 1700000000.0 + i
            ));
        }

        await _repository.BulkImportAsync(Array.Empty<FolderItem>(), scores);
    }

    [Fact]
    public async Task IncrementalPaging_1000Rows_LoadsAllPagesWithoutDuplicates()
    {
        await PopulateScoresAsync(1000);

        var vm = new LibraryViewModel(_repository, _fileService, _folderService, _libraryService);
        vm.PageSize = 100;
        await vm.InitializeAsync();

        // 1. Initial page
        Assert.Equal(100, vm.DisplayedScores.Count);
        Assert.Equal(1000, vm.TotalItemCount);
        Assert.True(vm.HasMoreItems);

        // 2. Load next page
        await vm.LoadNextPageAsync();
        Assert.Equal(200, vm.DisplayedScores.Count);
        Assert.True(vm.HasMoreItems);

        // Check no duplicates in first 200
        var idSet = new HashSet<string>();
        foreach (var item in vm.DisplayedScores)
        {
            Assert.True(idSet.Add(item.Id), $"Duplicate item found: {item.Id}");
        }

        // 3. Load all remaining pages
        while (vm.HasMoreItems)
        {
            await vm.LoadNextPageAsync();
        }

        Assert.Equal(1000, vm.DisplayedScores.Count);
        Assert.False(vm.HasMoreItems);

        // Verify total uniqueness across all 1,000 items
        idSet.Clear();
        foreach (var item in vm.DisplayedScores)
        {
            Assert.True(idSet.Add(item.Id), $"Duplicate item found: {item.Id}");
        }
    }

    [Fact]
    public async Task SearchAndFavorites_PagingWorksBeyond200Items()
    {
        await PopulateScoresAsync(1000);

        var vm = new LibraryViewModel(_repository, _fileService, _folderService, _libraryService);
        vm.PageSize = 100;
        await vm.InitializeAsync();

        // 1. Favorites view (333 items)
        await vm.NavigateToFavoritesCommand.ExecuteAsync(null);
        Assert.Equal(100, vm.DisplayedScores.Count);
        Assert.Equal(333, vm.TotalItemCount);
        Assert.True(vm.HasMoreItems);

        await vm.LoadNextPageAsync();
        Assert.Equal(200, vm.DisplayedScores.Count);

        while (vm.HasMoreItems)
        {
            await vm.LoadNextPageAsync();
        }
        Assert.Equal(333, vm.DisplayedScores.Count);

        // 2. Search view ("anime" matches 500 items)
        vm.SearchText = "anime";
        await vm.ReloadQueryAsync();

        Assert.Equal(100, vm.DisplayedScores.Count);
        Assert.Equal(500, vm.TotalItemCount);
        Assert.True(vm.HasMoreItems);

        while (vm.HasMoreItems)
        {
            await vm.LoadNextPageAsync();
        }
        Assert.Equal(500, vm.DisplayedScores.Count);
    }
}
