using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using RobloxPiano.Core.Importing;
using RobloxPiano.Core.Library;
using RobloxPiano.Core.Services;
using RobloxPiano.Infrastructure.Data;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class ImportLibraryIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly string _storageRoot;
    private readonly string _sourceDir;
    private readonly SqliteLibraryRepository _repository;
    private readonly LibraryFileService _fileService;
    private readonly FolderService _folderService;
    private readonly LibraryService _libraryService;
    private readonly ImportPipeline _pipeline;

    public ImportLibraryIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"rp_import_lib_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _dbPath = Path.Combine(_tempDir, "test_library.db");
        _storageRoot = Path.Combine(_tempDir, "Storage");
        _sourceDir = Path.Combine(_tempDir, "ExternalSource");

        Directory.CreateDirectory(_storageRoot);
        Directory.CreateDirectory(_sourceDir);

        _repository = new SqliteLibraryRepository(_dbPath);
        _fileService = new LibraryFileService(_storageRoot);
        _folderService = new FolderService(_repository, _fileService);
        _libraryService = new LibraryService(_repository, _fileService, _folderService);
        _pipeline = new ImportPipeline(_libraryService, _repository);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    private string CreateMidi(string filename, int pitch = 60)
    {
        string path = Path.Combine(_sourceDir, filename);
        var midiFile = new MidiFile(new TrackChunk(
            new NoteOnEvent((SevenBitNumber)pitch, (SevenBitNumber)64) { DeltaTime = 0 },
            new NoteOffEvent((SevenBitNumber)pitch, (SevenBitNumber)0) { DeltaTime = 480 }
        ))
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480)
        };
        midiFile.Write(path, true);
        return path;
    }

    private string CreateMml(string filename, string content)
    {
        string path = Path.Combine(_sourceDir, filename);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task ImportToLibrary_ValidMidi_CreatesScoreEntry()
    {
        await _repository.InitializeAsync();
        string midiPath = CreateMidi("test_song.mid", 60);

        var req = new ImportRequest(midiPath, addToLibrary: true);
        var result = await _pipeline.ImportFileAsync(req);

        Assert.True(result.Success);
        Assert.NotNull(result.CreatedScoreItem);

        var dbScore = await _repository.GetScoreAsync(result.CreatedScoreItem.Id);
        Assert.NotNull(dbScore);
        Assert.Equal("test_song", dbScore.Title);
        Assert.Equal("MIDI", dbScore.SourceType);
        Assert.True(File.Exists(midiPath)); // Original file preserved
        Assert.True(File.Exists(dbScore.FilePath)); // Managed copy exists
    }

    [Fact]
    public async Task ImportToLibrary_ValidMml_CreatesScoreEntry()
    {
        await _repository.InitializeAsync();
        string mmlPath = CreateMml("rhythm.mml", "MML@T120L4CDEF;");

        var req = new ImportRequest(mmlPath, addToLibrary: true);
        var result = await _pipeline.ImportFileAsync(req);

        Assert.True(result.Success);
        Assert.NotNull(result.CreatedScoreItem);

        var dbScore = await _repository.GetScoreAsync(result.CreatedScoreItem.Id);
        Assert.NotNull(dbScore);
        Assert.Equal("rhythm", dbScore.Title);
        Assert.Equal("MML", dbScore.SourceType);
        Assert.True(File.Exists(mmlPath)); // Original file preserved
    }

    [Fact]
    public async Task ImportToLibrary_FailedParse_CreatesNoRow()
    {
        await _repository.InitializeAsync();
        string badPath = Path.Combine(_sourceDir, "broken.mid");
        File.WriteAllText(badPath, "NOT A MIDI FILE");

        var req = new ImportRequest(badPath, addToLibrary: true);
        var result = await _pipeline.ImportFileAsync(req);

        Assert.False(result.Success);

        var allScores = await _repository.GetAllScoresAsync();
        Assert.Empty(allScores);
    }

    [Fact]
    public async Task ImportToLibrary_DuplicatePath_UsesDefinedPolicy()
    {
        await _repository.InitializeAsync();
        string midiPath = CreateMidi("unique.mid", 60);

        // 1st import
        var req1 = new ImportRequest(midiPath, addToLibrary: true);
        var result1 = await _pipeline.ImportFileAsync(req1);
        Assert.True(result1.Success);

        // 2nd import of same file
        var req2 = new ImportRequest(midiPath, addToLibrary: true);
        var result2 = await _pipeline.ImportFileAsync(req2);

        // Policy: reject duplicate with clear message
        Assert.False(result2.Success);
        Assert.Equal(ImportError.AlreadyImported, result2.ErrorMessage);

        var allScores = await _repository.GetAllScoresAsync();
        Assert.Single(allScores); // Only 1 row in DB
    }

    [Fact]
    public async Task ImportToLibrary_BatchPartialFailure_NoCorruptRows()
    {
        await _repository.InitializeAsync();
        string validMidi = CreateMidi("good.mid", 60);
        string corruptMidi = Path.Combine(_sourceDir, "bad.mid");
        File.WriteAllText(corruptMidi, "bad data");
        string validMml = CreateMml("good.mml", "MML@T120L4C;");

        var requests = new[]
        {
            new ImportRequest(validMidi, addToLibrary: true),
            new ImportRequest(corruptMidi, addToLibrary: true),
            new ImportRequest(validMml, addToLibrary: true)
        };

        var batchResult = await _pipeline.ImportBatchAsync(requests);

        Assert.Equal(3, batchResult.TotalCount);
        Assert.Equal(2, batchResult.SuccessCount);
        Assert.Equal(1, batchResult.FailureCount);

        var allScores = await _repository.GetAllScoresAsync();
        Assert.Equal(2, allScores.Count);
        Assert.Contains(allScores, s => s.Title == "good" && s.SourceType == "MIDI");
        Assert.Contains(allScores, s => s.Title == "good" && s.SourceType == "MML");
    }
}
