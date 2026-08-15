using RobloxPiano.Core.Importers;
using RobloxPiano.Core.Library;
using RobloxPiano.Core.Music;
using RobloxPiano.Core.Piano;
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
        ct.ThrowIfCancellationRequested();

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

        ct.ThrowIfCancellationRequested();

        // 1. Detection & Size Guard
        var (sourceType, detectError) = ImportFileDetector.Detect(normalizedPath);
        if (sourceType == ImportSourceType.Unknown || detectError != null)
        {
            return ImportResult.Failed(normalizedPath, detectError ?? ImportError.UnsupportedFormat, sourceType: sourceType);
        }

        ct.ThrowIfCancellationRequested();

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
        catch (OperationCanceledException)
        {
            throw;
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

        ct.ThrowIfCancellationRequested();

        // 3. Centralized Timeline Validation (strict BPM, timing, and note validation)
        var validation = ImportTimelineValidator.Validate(timeline, request.TargetPianoProfile);
        if (!validation.IsValid)
        {
            return ImportResult.Failed(normalizedPath, validation.ErrorMessage ?? ImportError.CorruptTiming, errorCode: "TIMELINE_INVALID", sourceType: sourceType);
        }

        // 4. Title Normalization
        string normalizedTitle = NormalizeTitle(timeline.Title, request.PreferredTitle, normalizedPath);
        timeline.Title = normalizedTitle;

        ct.ThrowIfCancellationRequested();

        // 5. Optional Library Persistence
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

                ct.ThrowIfCancellationRequested();

                createdScore = await _libraryService.ImportExternalFileAsync(
                    normalizedPath,
                    request.TargetFolderId,
                    sourceType: sourceType == ImportSourceType.Midi ? "MIDI" : "MML",
                    ct: ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return ImportResult.Failed(normalizedPath, $"라이브러리 등록 실패: {ex.Message}", errorCode: "LIBRARY_PERSIST_ERROR", sourceType: sourceType);
            }
        }

        ct.ThrowIfCancellationRequested();

        return ImportResult.Successful(
            normalizedPath,
            sourceType,
            normalizedTitle,
            timeline,
            validation.PlayableNotes,
            validation.OutOfRangeNotes,
            validation.MinPitch,
            validation.MaxPitch,
            createdScore);
    }

    public async Task<ImportResult> ImportTextAsync(
        string mmlText,
        string? preferredTitle = null,
        bool addToLibrary = true,
        PianoProfile? targetPianoProfile = null,
        string? targetFolderId = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(mmlText))
        {
            return ImportResult.Failed("text://pasted-mml", "MML 텍스트가 비어 있습니다.", errorCode: "EMPTY_TEXT", sourceType: ImportSourceType.Mml);
        }

        ct.ThrowIfCancellationRequested();

        var smartResult = SmartMmlPreprocessor.Process(mmlText);
        if (!smartResult.Success)
        {
            return ImportResult.Failed("text://pasted-mml", smartResult.ErrorMessage ?? "MML 전처리에 실패했습니다.", errorCode: "SMART_MML_ERROR", sourceType: ImportSourceType.Mml);
        }

        string cleanMml = smartResult.ProcessedMml;
        string? effectiveTitle = preferredTitle ?? smartResult.ExtractedTitle;

        MusicTimeline timeline;
        try
        {
            timeline = _mmlImporter.ImportScore(cleanMml);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (MmlParseException ex)
        {
            return ImportResult.Failed("text://pasted-mml", $"{ImportError.InvalidMml}: {ex.Message}", errorCode: "MML_SYNTAX", sourceType: ImportSourceType.Mml);
        }
        catch (Exception ex)
        {
            return ImportResult.Failed("text://pasted-mml", $"{ImportError.InvalidMml} ({ex.Message})", errorCode: "PARSE_ERROR", sourceType: ImportSourceType.Mml);
        }

        ct.ThrowIfCancellationRequested();

        var validation = ImportTimelineValidator.Validate(timeline, targetPianoProfile);
        if (!validation.IsValid)
        {
            return ImportResult.Failed("text://pasted-mml", validation.ErrorMessage ?? ImportError.CorruptTiming, errorCode: "TIMELINE_INVALID", sourceType: ImportSourceType.Mml);
        }

        string normalizedTitle = NormalizeTitle(timeline.Title, effectiveTitle, "붙여넣은 MML.mml");
        timeline.Title = normalizedTitle;

        ct.ThrowIfCancellationRequested();

        ScoreItem? createdScore = null;
        if (addToLibrary && _libraryService != null)
        {
            try
            {
                createdScore = await _libraryService.CreateScoreFromTextAsync(
                    cleanMml,
                    normalizedTitle,
                    targetFolderId,
                    ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return ImportResult.Failed("text://pasted-mml", $"라이브러리 등록 실패: {ex.Message}", errorCode: "LIBRARY_PERSIST_ERROR", sourceType: ImportSourceType.Mml);
            }
        }

        ct.ThrowIfCancellationRequested();

        return ImportResult.Successful(
            createdScore?.FilePath ?? "text://pasted-mml",
            ImportSourceType.Mml,
            normalizedTitle,
            timeline,
            validation.PlayableNotes,
            validation.OutOfRangeNotes,
            validation.MinPitch,
            validation.MaxPitch,
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
