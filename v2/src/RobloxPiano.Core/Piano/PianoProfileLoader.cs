using System.Reflection;
using System.Text.Json;

namespace RobloxPiano.Core.Piano;

public static class PianoProfileLoader
{
    public static PianoProfile LoadProfileFromJson(string jsonText, string? filePath = null)
    {
        using var doc = JsonDocument.Parse(jsonText);
        var root = doc.RootElement;

        var name = root.TryGetProperty("name", out var pName) ? pName.GetString() ?? "Unknown Profile" : "Unknown Profile";
        var description = root.TryGetProperty("description", out var pDesc) ? pDesc.GetString() ?? "" : "";
        var version = root.TryGetProperty("version", out var pVer) ? pVer.GetString() ?? "1.0" : "1.0";
        var minPitch = root.TryGetProperty("min_pitch", out var pMin) ? pMin.GetInt32() : 36;
        var maxPitch = root.TryGetProperty("max_pitch", out var pMax) ? pMax.GetInt32() : 96;
        var sustainPedal = root.TryGetProperty("sustain_pedal", out var pSus) ? pSus.GetString() : null;

        var keysDict = new Dictionary<int, KeyMapping>();

        if (root.TryGetProperty("keys", out var keysElem) && keysElem.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in keysElem.EnumerateObject())
            {
                if (!int.TryParse(prop.Name, out int pitch))
                    continue;

                var kData = prop.Value;
                var charVal = kData.TryGetProperty("char", out var cVal) ? cVal.GetString() ?? "" : "";
                var physicalKey = kData.TryGetProperty("physical_key", out var pkVal) ? pkVal.GetString() ?? "" : "";
                var keyName = kData.TryGetProperty("name", out var knVal) ? knVal.GetString() ?? "" : "";

                var modifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (kData.TryGetProperty("modifiers", out var modsElem) && modsElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in modsElem.EnumerateArray())
                    {
                        var mStr = m.GetString();
                        if (!string.IsNullOrEmpty(mStr))
                        {
                            modifiers.Add(mStr);
                        }
                    }
                }

                // Legacy shift migration
                if (kData.TryGetProperty("shift", out var shiftElem) && shiftElem.ValueKind == JsonValueKind.True)
                {
                    if (!modifiers.Contains("SHIFT"))
                    {
                        modifiers.Add("SHIFT");
                    }
                }

                keysDict[pitch] = new KeyMapping(pitch, charVal, physicalKey, modifiers, keyName);
            }
        }

        return new PianoProfile(
            name: name,
            description: description,
            version: version,
            minPitch: minPitch,
            maxPitch: maxPitch,
            keys: keysDict,
            sustainPedal: sustainPedal,
            filePath: filePath
        );
    }

    public static PianoProfile LoadProfile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Piano profile file not found: {filePath}", filePath);

        var json = File.ReadAllText(filePath);
        return LoadProfileFromJson(json, filePath);
    }

    public static PianoProfile LoadEmbeddedProfile(string profileFileName)
    {
        var asm = typeof(PianoProfileLoader).Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(r => r.EndsWith(profileFileName, StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
        {
            // Fallback: check file system if running in dev
            var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Profiles", profileFileName);
            if (File.Exists(localPath))
            {
                return LoadProfile(localPath);
            }

            throw new InvalidOperationException($"Embedded profile resource not found: {profileFileName}");
        }

        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new InvalidOperationException($"Failed to open stream for resource: {resourceName}");

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return LoadProfileFromJson(json, profileFileName);
    }

    public static PianoProfile Load61KeyProfile()
    {
        return LoadEmbeddedProfile("roblox_virtual_piano_61.json");
    }

    public static PianoProfile Load88KeyProfile()
    {
        return LoadEmbeddedProfile("roblox_virtual_piano_88.json");
    }

    public static PianoProfile LoadDefaultProfile()
    {
        return Load88KeyProfile();
    }
}
