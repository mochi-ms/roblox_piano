using System.Windows.Controls;
using RobloxPiano.Desktop.ViewModels;

namespace RobloxPiano.Desktop.Views
{
    public partial class LibraryView : UserControl
    {
        public LibraryView()
        {
            InitializeComponent();
        }

        private void SortButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void DataGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange > 0 && DataContext is LibraryViewModel vm)
            {
                var scrollViewer = e.OriginalSource as ScrollViewer;
                if (scrollViewer != null)
                {
                    // If scrolled near the bottom, trigger loading the next page
                    if (scrollViewer.VerticalOffset + scrollViewer.ViewportHeight >= scrollViewer.ExtentHeight - 20)
                    {
                        if (vm.HasMoreItems && !vm.IsLoadingMore && !vm.IsLoading)
                        {
                            _ = vm.LoadNextPageCommand.ExecuteAsync(null);
                        }
                    }
                }
            }
        }
    }
}
