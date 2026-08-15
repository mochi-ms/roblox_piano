namespace RobloxPiano.Playback.Windows.Input;

public static class VirtualKeyMap
{
    private static readonly Dictionary<string, ushort> ScancodeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Number row
        { "1", 0x02 }, { "2", 0x03 }, { "3", 0x04 }, { "4", 0x05 }, { "5", 0x06 },
        { "6", 0x07 }, { "7", 0x08 }, { "8", 0x09 }, { "9", 0x0A }, { "0", 0x0B },

        // Top letter row
        { "q", 0x10 }, { "w", 0x11 }, { "e", 0x12 }, { "r", 0x13 }, { "t", 0x14 },
        { "y", 0x15 }, { "u", 0x16 }, { "i", 0x17 }, { "o", 0x18 }, { "p", 0x19 },

        // Home letter row
        { "a", 0x1E }, { "s", 0x1F }, { "d", 0x20 }, { "f", 0x21 }, { "g", 0x22 },
        { "h", 0x23 }, { "j", 0x24 }, { "k", 0x25 }, { "l", 0x26 },

        // Bottom letter row
        { "z", 0x2C }, { "x", 0x2D }, { "c", 0x2E }, { "v", 0x2F }, { "b", 0x30 },
        { "n", 0x31 }, { "m", 0x32 },

        // Punctuation
        { ";", 0x27 }, { "=", 0x0D }, { ",", 0x33 }, { "-", 0x0C },
        { ".", 0x34 }, { "/", 0x35 }, { "`", 0x29 }, { "[", 0x1A },
        { "\\", 0x2B }, { "]", 0x1B }, { "'", 0x28 },

        // Modifiers & Controls
        { "shift", 0x2A },
        { "lshift", 0x2A },
        { "rshift", 0x36 },
        { "ctrl", 0x1D },
        { "lctrl", 0x1D },
        { "alt", 0x38 },
        { "lalt", 0x38 },
        { "space", 0x39 },
        { "vk_20", 0x39 },
        { " ", 0x39 },
        { "enter", 0x1C },
        { "esc", 0x01 }
    };

    public static ushort GetScancode(string key)
    {
        if (string.IsNullOrEmpty(key)) return 0;

        var keyLower = key.ToLowerInvariant();
        if (ScancodeMap.TryGetValue(keyLower, out var scan))
        {
            return scan;
        }

        // Dynamic fallback: VkKeyScanW -> MapVirtualKeyW
        if (key.Length == 1)
        {
            try
            {
                short vkRes = SendInputNative.VkKeyScanW(key[0]);
                if (vkRes != -1)
                {
                    uint vk = (uint)(vkRes & 0xFF);
                    uint dynScan = SendInputNative.MapVirtualKeyW(vk, 0);
                    if (dynScan > 0)
                    {
                        return (ushort)dynScan;
                    }
                }
            }
            catch
            {
                // Fallback failed
            }
        }

        return 0;
    }
}
