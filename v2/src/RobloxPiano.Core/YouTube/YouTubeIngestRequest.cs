namespace RobloxPiano.Core.YouTube;

public record YouTubeIngestRequest(
    string JobId,
    string Url,
    string? OutputWorkspaceRoot = null
);
