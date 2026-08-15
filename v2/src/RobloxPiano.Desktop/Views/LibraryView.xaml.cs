using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RobloxPiano.Desktop.ViewModels;

namespace RobloxPiano.Desktop.Views
{
    public partial class LibraryView : UserControl
    {
        public LibraryView()
        {
            InitializeComponent();
        }

        private void SortButton_Click(object sender, RoutedEventArgs e)
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
                if (e.OriginalSource is ScrollViewer scrollViewer)
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

        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is LibraryViewModel vm && vm.SelectedScore != null)
            {
                vm.OpenSelectedScore();
            }
        }

        private void DataGridRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGridRow row && row.Item is ScoreItemViewModel scoreVm && DataContext is LibraryViewModel vm)
            {
                row.IsSelected = true;
                vm.SelectedScore = scoreVm;
            }
        }

        private void FolderButton_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is FolderItemViewModel folderVm && DataContext is LibraryViewModel vm)
            {
                vm.SelectedFolder = folderVm;
            }
        }

        private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Focus guard: if focus is within an editable text input, do NOT intercept
            var focusedElement = Keyboard.FocusedElement;
            if (focusedElement is TextBox || focusedElement is PasswordBox || focusedElement is RichTextBox)
            {
                return;
            }

            if (DataContext is not LibraryViewModel vm)
            {
                return;
            }

            if (e.Key == Key.F2)
            {
                if (vm.CanRenameSelectedItem)
                {
                    _ = vm.RenameSelectedItemCommand.ExecuteAsync(null);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Delete)
            {
                if (vm.CanDeleteSelectedItem)
                {
                    _ = vm.DeleteSelectedItemCommand.ExecuteAsync(null);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                if (vm.SelectedScore != null)
                {
                    vm.OpenSelectedScore();
                    e.Handled = true;
                }
                else if (vm.SelectedFolder != null && !vm.IsFavoritesView)
                {
                    _ = vm.NavigateToFolderCommand.ExecuteAsync(vm.SelectedFolder.Id);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (vm.CanCopySelectedItem)
                {
                    vm.CopySelectedScore();
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.X && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (vm.CanCutSelectedItem)
                {
                    vm.CutSelectedScore();
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (vm.CanPaste)
                {
                    _ = vm.PasteScoreCommand.ExecuteAsync(null);
                    e.Handled = true;
                }
            }
        }
    }
}
