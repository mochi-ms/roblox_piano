using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RobloxPiano.Desktop;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    // DWMWA_USE_IMMERSIVE_DARK_MODE: 20 on Windows 11 / Windows 10 (20H1+), 19 on older Windows 10
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                int useImmersiveDarkMode = 1;
                // Try modern attribute 20 first, fallback to 19
                if (DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref useImmersiveDarkMode, sizeof(int));
                }
            }
        }
        catch
        {
            // Graceful fallback on non-Windows/legacy environments
        }
    }
}