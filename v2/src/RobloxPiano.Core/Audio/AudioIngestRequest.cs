namespace RobloxPiano.Core.Audio;

public class AudioIngestRequest
{
    public string FilePath { get; }
    public string JobId { get; }
    public string? CustomWorkspaceDir { get; }

    public AudioIngestRequest(string filePath, string? jobId = null, string? customWorkspaceDir = null)
    {
        FilePath = filePath;
        JobId = !string.IsNullOrWhiteSpace(jobId) ? jobId : Guid.NewGuid().ToString("N");
        CustomWorkspaceDir = customWorkspaceDir;
    }
}
