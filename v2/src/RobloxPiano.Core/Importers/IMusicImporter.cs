using RobloxPiano.Core.Music;

namespace RobloxPiano.Core.Importers;

public interface IMusicImporter
{
    IReadOnlyList<string> SupportedExtensions { get; }
    bool CanImport(string filePathOrContent);
    MusicTimeline ImportScore(string filePathOrContent, IDictionary<string, object>? options = null);
}
