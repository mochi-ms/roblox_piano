using RobloxPiano.Core.Piano;

namespace RobloxPiano.Core.Importing;

public class ImportRequest
{
    public string FilePath { get; set; } = string.Empty;
    public string? PreferredTitle { get; set; }
    public string? TargetFolderId { get; set; }
    public bool AddToLibrary { get; set; } = true;
    public PianoProfile? TargetPianoProfile { get; set; }

    public ImportRequest() { }

    public ImportRequest(
        string filePath,
        string? preferredTitle = null,
        string? targetFolderId = null,
        bool addToLibrary = true,
        PianoProfile? targetPianoProfile = null)
    {
        FilePath = filePath;
        PreferredTitle = preferredTitle;
        TargetFolderId = targetFolderId;
        AddToLibrary = addToLibrary;
        TargetPianoProfile = targetPianoProfile;
    }
}
