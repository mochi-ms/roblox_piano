using System.Runtime.InteropServices;

namespace RobloxPiano.Playback.Windows.Input;

public class WindowsSendInputBackend : IPlaybackBackend, ITargetedPlaybackBackend
{
    private readonly HashSet<ushort> _pressedScancodes = new();
    private readonly object _lock = new();

    public void KeyDown(string key)
    {
        ushort scancode = VirtualKeyMap.GetScancode(key);
        if (scancode > 0)
        {
            lock (_lock)
            {
                SendScancode(scancode, isUp: false);
                _pressedScancodes.Add(scancode);
            }
        }
    }

    public void KeyUp(string key)
    {
        ushort scancode = VirtualKeyMap.GetScancode(key);
        if (scancode > 0)
        {
            lock (_lock)
            {
                SendScancode(scancode, isUp: true);
                _pressedScancodes.Remove(scancode);
            }
        }
    }

    public void ReleaseAll()
    {
        lock (_lock)
        {
            foreach (var scancode in _pressedScancodes.ToList())
            {
                try
                {
                    SendScancode(scancode, isUp: true);
                }
                catch
                {
                    // Best-effort release
                }
            }
            _pressedScancodes.Clear();

            // Extra safety: unconditionally release Shift, Ctrl, Alt
            try { SendScancode(0x2A, isUp: true); } catch { } // LShift
            try { SendScancode(0x36, isUp: true); } catch { } // RShift
            try { SendScancode(0x1D, isUp: true); } catch { } // Ctrl
            try { SendScancode(0x38, isUp: true); } catch { } // Alt
            try { SendScancode(0x39, isUp: true); } catch { } // Space
        }
    }

    private static void SendScancode(ushort scancode, bool isUp)
    {
        uint flags = SendInputNative.KEYEVENTF_SCANCODE;
        if (isUp)
        {
            flags |= SendInputNative.KEYEVENTF_KEYUP;
        }

        var input = new SendInputNative.INPUT
        {
            type = SendInputNative.INPUT_KEYBOARD,
            u = new SendInputNative.INPUTUNION
            {
                ki = new SendInputNative.KEYBDINPUT
                {
                    wVk = 0,
                    wScan = scancode,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };

        var inputs = new[] { input };
        uint result = SendInputNative.SendInput(1, inputs, Marshal.SizeOf<SendInputNative.INPUT>());
        if (result != 1)
        {
            int err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"SendInput failed. Expected 1 event, returned {result}. Win32 Error: {err}");
        }
    }

    public void Dispose()
    {
        ReleaseAll();
    }
}
