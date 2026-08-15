namespace RobloxPiano.Core.Importing;

public interface IImportPipeline
{
    Task<ImportResult> ImportFileAsync(ImportRequest request, CancellationToken ct = default);

    Task<ImportBatchResult> ImportBatchAsync(
        IReadOnlyList<ImportRequest> requests,
        IProgress<(int Current, int Total, string FileName)>? progress = null,
        CancellationToken ct = default);
}
