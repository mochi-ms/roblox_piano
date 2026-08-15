using RobloxPiano.Core.Importers;
using RobloxPiano.Core.Library;
using RobloxPiano.Core.Music;
using RobloxPiano.Core.Services;

namespace RobloxPiano.Core.Importing;

public class ImportPipeline : IImportPipeline
{
    private readonly MidiImporter _midiImporter;
    private readonly MmlImporter _mmlImporter;
    private readonly LibraryService? _libraryService;
    private readonly ILibraryRepository? _repository;

    public ImportPipeline(
        LibraryService? libraryService = null,
        ILibraryRepository? repository = null,
        MidiImporter? midiImporter = null,
        MmlImporter? mmlImporter = null)
    {
        _libraryService = libraryService;
        _repository = repository ?? libraryService?.Repository;
        _midiImporter = midiImporter ?? new MidiImporter();
        _mmlImporter = mmlImporter ?? new MmlImporter();
    }

    public async Task<ImportResult> ImportFileAsync(ImportRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.FilePath))
        {
            return ImportResult.Failed(request.FilePath ?? string.Empty, ImportError.FileNotFound);
        }

        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(request.FilePath);
        }
        catch (Exception ex)
        {
            return ImportResult.Failed(request.FilePath, $"{ImportError.FileNotFound}: {ex.Message}");
        }

        // 1. Detection & Size Guard
        var (sourceType, detectError) = ImportFileDetector.Detect(normalizedPath);
        if (sourceType == ImportSourceType.Unknown || detectError != null)
        {
            return ImportResult.Failed(normalizedPath, detectError ?? ImportError.UnsupportedFormat, sourceType: sourceType);
        }

        // 2. Parse using existing Phase 2 Importers
        MusicTimeline timeline;
        try
        {
            if (sourceType == ImportSourceType.Midi)
            {
                timeline = _midiImporter.ImportScore(normalizedPath);
            }
            else if (sourceType == ImportSourceType.Mml)
            {
                timeline = _mmlImporter.ImportScore(normalizedPath);
            }
            else
            {
                return ImportResult.Failed(normalizedPath, ImportError.UnsupportedFormat, sourceType: sourceType);
            }
        }
        catch (MmlParseException ex)
        {
            return ImportResult.Failed(normalizedPath, $"{ImportError.InvalidMml}: {ex.Message}", errorCode: "MML_SYNTAX", sourceType: sourceType);
        }
        catch (Exception ex)
        {
            string err = sourceType == ImportSourceType.Midi ? ImportError.CorruptMidi : ImportError.InvalidMml;
            return ImportResult.Failed(normalizedPath, $"{err} ({ex.Message})", errorCode: "PARSE_ERROR", sourceType: sourceType);
        }

        // 3. Timeline Validation
        if (timeline == null)
        {
            return ImportResult.Failed(normalizedPath, "타임라인을 생성할 수 없습니다.", sourceType: sourceType);
        }

        if (timeline.Notes.Count == 0)
        {
            return ImportResult.Failed(normalizedPath, ImportError.NoPlayableNotes, errorCode: "NO_NOTES", sourceType: sourceType);
        }

        // Structural note validation (finite, non-negative, valid pitches)
        foreach (var note in timeline.Notes)
        {
            if (double.IsNaN(note.StartTime) || double.IsInfinity(note.StartTime) || note.StartTime < 0 ||
                double.IsNaN(note.EndTime) || double.IsInfinity(note.EndTime) || note.EndTime <= note.StartTime ||
                note.Pitch < 0 || note.Pitch > 127)
            {
                return ImportResult.Failed(normalizedPath, ImportError.CorruptTiming, errorCode: "CORRUPT_TIMING", sourceType: sourceType);
            }
        }

        if (double.IsNaN(timeline.Duration) || double.IsInfinity(timeline.Duration) || timeline.Duration < 0)
        {
            return ImportResult.Failed(normalizedPath, ImportError.CorruptTiming, errorCode: "CORRUPT_TIMING", sourceType: sourceType);
        }

        if (double.IsNaN(timeline.InitialBpm) || double.IsInfinity(timeline.InitialBpm) || timeline.InitialBpm <= 0)
        {
            timeline.InitialBpm = 120.0;
        }

        // 4. Roblox 61-Key Diagnostics (Range: 36..96)
        int playableNotes = timeline.Notes.Count(n => n.Pitch >= 36 && n.Pitch <= 96);
        int outOfRangeNotes = timeline.Notes.Count(n => n.Pitch < 36 || n.Pitch > 96);
        var (minPitch, maxPitch) = timeline.PitchRange;

        // 5. Title Normalization
        string normalizedTitle = NormalizeTitle(timeline.Title, request.PreferredTitle, normalizedPath);
        timeline.Title = normalizedTitle;

        // 6. Optional Library Persistence
        ScoreItem? createdScore = null;
        if (request.AddToLibrary && _libraryService != null)
        {
            try
            {
                // Check duplicate path
                if (_repository != null)
                {
                    var existingScores = await _repository.GetAllScoresAsync(ct);
                    bool alreadyExists = existingScores.Any(s =>
                        string.Equals(s.SourceUrl, normalizedPath, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(s.FilePath, normalizedPath, StringComparison.OrdinalIgnoreCase));

                    if (alreadyExists)
                    {
                        return ImportResult.Failed(normalizedPath, ImportError.AlreadyImported, errorCode: "ALREADY_IMPORTED", sourceType: sourceType);
                    }
                }

                createdScore = await _libraryService.ImportExternalFileAsync(
                    normalizedPath,
                    request.TargetFolderId,
                    sourceType: sourceType == ImportSourceType.Midi ? "MIDI" : "MML",
                    ct: ct);
            }
            catch (Exception ex)
            {
                return ImportResult.Failed(normalizedPath, $"라이브러리 등록 실패: {ex.Message}", errorCode: "LIBRARY_PERSIST_ERROR", sourceType: sourceType);
            }
        }

        return ImportResult.Successful(
            normalizedPath,
            sourceType,
            normalizedTitle,
            timeline,
            playableNotes,
            outOfRangeNotes,
            minPitch,
            maxPitch,
            createdScore);
    }

    public async Task<ImportBatchResult> ImportBatchAsync(
        IReadOnlyList<ImportRequest> requests,
        IProgress<(int Current, int Total, string FileName)>? progress = null,
        CancellationToken ct = default)
    {
        var results = new List<ImportResult>();
        int total = requests.Count;

        for (int i = 0; i < total; i++)
        {
            var req = requests[i];
            string fname = Path.GetFileName(req.FilePath);

            if (ct.IsCancellationRequested)
            {
                // Mark current and remaining requests as cancelled
                for (int j = i; j < total; j++)
                {
                    results.Add(ImportResult.Failed(requests[j].FilePath, ImportError.Cancelled, errorCode: "CANCELLED"));
                }
                return new ImportBatchResult(results, isCancelled: true);
            }

            progress?.Report((i + 1, total, fname));

            try
            {
                var result = await ImportFileAsync(req, ct);
                results.Add(result);
            }
            catch (OperationCanceledException)
            {
                for (int j = i; j < total; j++)
                {
                    results.Add(ImportResult.Failed(requests[j].FilePath, ImportError.Cancelled, errorCode: "CANCELLED"));
                }
                return new ImportBatchResult(results, isCancelled: true);
            }
            catch (Exception ex)
            {
                results.Add(ImportResult.Failed(req.FilePath, $"처리 중 오류 발생: {ex.Message}", errorCode: "UNEXPECTED_ERROR"));
            }
        }

        return new ImportBatchResult(results, isCancelled: false);
    }

    private static string NormalizeTitle(string? timelineTitle, string? preferredTitle, string filePath)
    {
        if (!string.IsNullOrWhiteSpace(timelineTitle) &&
            !string.Equals(timelineTitle, "Untitled", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(timelineTitle, "MML Score", StringComparison.OrdinalIgnoreCase))
        {
            return timelineTitle.Trim();
        }

        if (!string.IsNullOrWhiteSpace(preferredTitle))
        {
            return preferredTitle.Trim();
        }

        var fnameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
        if (!string.IsNullOrWhiteSpace(fnameWithoutExt))
        {
            return fnameWithoutExt.Trim();
        }

        return "제목 없음";
    }
}
