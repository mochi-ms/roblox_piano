using System;
using System.IO;
using System.Threading.Tasks;
using RobloxPiano.Core.Importing;
using RobloxPiano.Core.Library;
using RobloxPiano.Core.Piano;
using RobloxPiano.Core.Services;
using RobloxPiano.Desktop.ViewModels;
using RobloxPiano.Infrastructure.Data;
using Xunit;

namespace RobloxPiano.IntegrationTests
{
    public class PastedMmlImportAndStabilizationTests : IDisposable
    {
        private readonly string _testDir;
        private readonly string _dbPath;
        private readonly string _storageRoot;
        private readonly SqliteLibraryRepository _repo;
        private readonly LibraryFileService _fileService;
        private readonly FolderService _folderService;
        private readonly LibraryService _libraryService;
        private readonly ImportPipeline _pipeline;

        public PastedMmlImportAndStabilizationTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), $"RP_PastedMmlTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_testDir);
            _dbPath = Path.Combine(_testDir, "test_library.db");
            _storageRoot = Path.Combine(_testDir, "storage");
            Directory.CreateDirectory(_storageRoot);

            _repo = new SqliteLibraryRepository(_dbPath);
            _repo.InitializeAsync().GetAwaiter().GetResult();

            _fileService = new LibraryFileService(_storageRoot);
            _folderService = new FolderService(_repo, _fileService);
            _libraryService = new LibraryService(_repo, _fileService, _folderService);
            _pipeline = new ImportPipeline(_libraryService, _repo);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testDir))
                {
                    Directory.Delete(_testDir, true);
                }
            }
            catch { }
        }

        [Fact]
        public async Task ImportTextAsync_ValidStandardMml_SuccessfullyParsesAndPersists()
        {
            // Given: valid MML text
            string mml = "MML@T120O4L4CDEFGAB>C;";

            // When: imported via pipeline
            var result = await _pipeline.ImportTextAsync(mml, preferredTitle: "C Major Scale", addToLibrary: true);

            // Then: result is successful and metadata is extracted
            Assert.True(result.Success);
            Assert.Equal("C Major Scale", result.Title);
            Assert.NotNull(result.Timeline);
            Assert.True(result.NoteCount >= 8);
            Assert.True(result.PlayableNoteCount >= 8);
            Assert.NotNull(result.CreatedScoreItem);

            // Verify file exists on disk in managed storage
            Assert.True(File.Exists(result.CreatedScoreItem.FilePath));
            var savedContent = await File.ReadAllTextAsync(result.CreatedScoreItem.FilePath);
            Assert.Equal(mml, savedContent);

            // Verify record exists in DB
            var dbScore = await _repo.GetScoreAsync(result.CreatedScoreItem.Id);
            Assert.NotNull(dbScore);
            Assert.Equal("C Major Scale", dbScore.Title);
            Assert.Equal("MML", dbScore.SourceType);
        }

        [Fact]
        public async Task ImportTextAsync_MultiTrackMml_SuccessfullyParsesAllTracks()
        {
            // Given: multi-track MML
            string mml = "MML@T140O4L4CDEF,O3L4GAB>C,O2L1C;";

            // When: imported via pipeline
            var result = await _pipeline.ImportTextAsync(mml, preferredTitle: "MultiTrack Harmony", addToLibrary: true);

            // Then
            Assert.True(result.Success);
            Assert.Equal("MultiTrack Harmony", result.Title);
            Assert.NotNull(result.Timeline);
            Assert.True(result.Timeline.TrackNames.Count >= 3 || result.Timeline.Notes.Count >= 9);
        }

        [Fact]
        public async Task ImportTextAsync_EmptyOrWhitespace_FailsCleanly()
        {
            var result1 = await _pipeline.ImportTextAsync("", preferredTitle: "Empty");
            Assert.False(result1.Success);
            Assert.Equal("EMPTY_TEXT", result1.ErrorCode);

            var result2 = await _pipeline.ImportTextAsync("   \n\t  ", preferredTitle: "Spaces");
            Assert.False(result2.Success);
            Assert.Equal("EMPTY_TEXT", result2.ErrorCode);
        }

        [Fact]
        public async Task ImportTextAsync_CorruptedMml_FailsCleanlyWithoutCrash()
        {
            var result = await _pipeline.ImportTextAsync("MML@!@#$%^&*()_+NonSenseMML;;;", preferredTitle: "Broken");
            Assert.False(result.Success);
            Assert.NotNull(result.ErrorMessage);
        }

        [Fact]
        public async Task ImportViewModel_ImportPastedTextCommand_SuccessFlow()
        {
            // Given
            var profileContext = new PianoProfileContext();
            var vm = new ImportViewModel(_pipeline, profileContext);

            // When user switches to Text mode and pastes valid MML
            vm.SelectMode("Text");
            Assert.True(vm.IsTextMode);
            Assert.False(vm.IsFileMode);

            vm.PastedMmlTitle = "Direct Paste Test";
            vm.PastedMmlText = "MML@T120O4L4CDEFGAB>C;";

            await vm.ImportPastedTextCommand.ExecuteAsync(null);

            // Then: switches back to File mode with result item queued
            Assert.True(vm.IsFileMode);
            Assert.False(vm.HasTextImportError);
            Assert.Single(vm.QueueItems);
            Assert.True(vm.QueueItems[0].IsCompleted);
            Assert.Equal("Direct Paste Test", vm.QueueItems[0].Result?.Title);
        }

        [Fact]
        public async Task ImportViewModel_ImportPastedTextCommand_FailureRetainsInputForEditing()
        {
            // Given
            var profileContext = new PianoProfileContext();
            var vm = new ImportViewModel(_pipeline, profileContext);

            // When user pastes invalid MML
            vm.SelectMode("Text");
            vm.PastedMmlTitle = "Bad MML";
            vm.PastedMmlText = "MML@NotValidNotesAtAll;";

            await vm.ImportPastedTextCommand.ExecuteAsync(null);

            // Then: stays in Text mode with error message, preserving user's typed input
            Assert.True(vm.IsTextMode);
            Assert.True(vm.HasTextImportError);
            Assert.NotEmpty(vm.TextImportErrorMessage);
            Assert.Equal("MML@NotValidNotesAtAll;", vm.PastedMmlText);
        }

        [Fact]
        public async Task LibraryService_CreateScoreFromTextAsync_WritesSafeFileAndSavesDb()
        {
            // Given
            string mml = "MML@T100O5L8CDEFEDCR;";

            // When
            var score = await _libraryService.CreateScoreFromTextAsync(mml, "My Test MML Score");

            // Then
            Assert.NotNull(score);
            Assert.Equal("My Test MML Score", score.Title);
            Assert.Equal("MML", score.SourceType);
            Assert.True(File.Exists(score.FilePath));
            Assert.Equal(7, score.TotalNotes);
            Assert.Equal(100.0, score.Bpm);

            var dbScore = await _repo.GetScoreAsync(score.Id);
            Assert.NotNull(dbScore);
            Assert.Equal(score.FilePath, dbScore.FilePath);
        }
    }
}
