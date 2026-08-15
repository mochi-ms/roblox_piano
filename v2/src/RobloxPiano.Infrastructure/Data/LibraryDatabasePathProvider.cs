namespace RobloxPiano.Infrastructure.Data;

public static class LibraryDatabasePathProvider
{
    public static string GetDefaultDatabasePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "RobloxPianoPlayer", "library_v2.db");
    }

    public static string GetDefaultLibraryStorageRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "RobloxPianoPlayer", "Library");
    }

    public static string GetDefaultLegacyV1DatabasePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "RobloxPianoPlayer", "library.db");
    }

    public static string GetDefaultAudioWorkspaceRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "RobloxPianoPlayer", "AudioWorkspace");
    }
}
