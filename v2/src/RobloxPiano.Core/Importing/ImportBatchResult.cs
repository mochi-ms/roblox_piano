namespace RobloxPiano.Core.Importing;

public class ImportBatchResult
{
    public IReadOnlyList<ImportResult> Results { get; }
    public int TotalCount => Results.Count;
    public int SuccessCount => Results.Count(r => r.Success);
    public int FailureCount => Results.Count(r => !r.Success);
    public bool IsAllSuccessful => TotalCount > 0 && FailureCount == 0;
    public bool IsCancelled { get; set; }

    public ImportBatchResult(IReadOnlyList<ImportResult> results, bool isCancelled = false)
    {
        Results = results;
        IsCancelled = isCancelled;
    }
}
