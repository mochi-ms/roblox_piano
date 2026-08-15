using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using RobloxPiano.Desktop.ViewModels;

namespace RobloxPiano.Desktop.Views;

public partial class OverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public OverlayWindow(OverlayViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionTopRight();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT);
            }
        }
        catch
        {
            // Fallback for non-Windows environments
        }

        PositionTopRight();
    }

    private void PositionTopRight()
    {
        try
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - (ActualWidth > 0 ? ActualWidth : 320) - 24;
            Top = workArea.Top + 24;
        }
        catch
        {
            Left = 100;
            Top = 100;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverlayViewModel.IsVisible))
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (DataContext is OverlayViewModel vm)
                {
                    if (vm.IsVisible)
                    {
                        PositionTopRight();
                        Visibility = Visibility.Visible;
                    }
                    else
                    {
                        Visibility = Visibility.Collapsed;
                    }
                }
            });
        }
    }
}
