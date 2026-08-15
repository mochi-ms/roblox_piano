using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using RobloxPiano.Desktop.ViewModels;
using RobloxPiano.Desktop.Views;

namespace RobloxPiano.Desktop;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    // DWMWA_USE_IMMERSIVE_DARK_MODE: 20 on Windows 11 / Windows 10 (20H1+), 19 on older Windows 10
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

    private HwndSource? _hwndSource;
    private OverlayWindow? _overlayWindow;
    private HwndSourceHook? _hwndHook;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        Cleanup();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Cleanup();
    }

    private void Cleanup()
    {
        if (_overlayWindow != null)
        {
            try { _overlayWindow.Close(); } catch { }
            _overlayWindow = null;
        }

        if (_hwndSource != null && _hwndHook != null)
        {
            try { _hwndSource.RemoveHook(_hwndHook); } catch { }
            _hwndHook = null;
        }

        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
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

                _hwndSource = HwndSource.FromHwnd(handle);
                if (DataContext is MainViewModel mainVm)
                {
                    _hwndHook = (IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                    {
                        return mainVm.HotkeyService.ProcessWindowMessage(hwnd, msg, wParam, lParam, ref handled);
                    };
                    _hwndSource?.AddHook(_hwndHook);
                    mainVm.HotkeyService.RegisterHotkeys(handle);

                    _overlayWindow = new OverlayWindow(mainVm.PlayerViewModel.OverlayViewModel);
                }
            }
        }
        catch
        {
            // Graceful fallback on non-Windows/legacy environments
        }
    }
}