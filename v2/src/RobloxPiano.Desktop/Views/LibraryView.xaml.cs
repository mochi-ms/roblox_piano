using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using RobloxPiano.Desktop.ViewModels;

namespace RobloxPiano.Desktop.Views
{
    public partial class LibraryView : UserControl
    {
        private Point _dragStartPoint;
        private bool _isDraggingInternalScores;

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

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is LibraryViewModel vm && sender is DataGrid dg)
            {
                var selected = dg.SelectedItems.OfType<ScoreItemViewModel>().ToList();
                vm.UpdateSelectedScores(selected);
            }
        }

        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // If double click was on a DataGridRow
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && dep is not DataGridRow)
            {
                dep = VisualTreeHelper.GetParent(dep);
            }

            if (dep is DataGridRow && DataContext is LibraryViewModel vm && vm.SelectedScore != null)
            {
                vm.OpenSelectedScore();
                e.Handled = true;
            }
        }

        private void DataGridRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGridRow row && row.Item is ScoreItemViewModel scoreVm && DataContext is LibraryViewModel vm)
            {
                // If row is not currently selected, select only this row
                if (!row.IsSelected)
                {
                    ScoresDataGrid.SelectedItems.Clear();
                    row.IsSelected = true;
                    vm.SelectedScore = scoreVm;
                    vm.UpdateSelectedScores(new[] { scoreVm });
                }
            }
        }

        private void ContentArea_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // If click was on background outside of rows or buttons, clear selection
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null)
            {
                if (dep is DataGridRow || dep is Button || dep is TextBox || dep is ScrollBar)
                    return;
                dep = VisualTreeHelper.GetParent(dep);
            }

            if (DataContext is LibraryViewModel vm)
            {
                ScoresDataGrid.SelectedItems.Clear();
                vm.ClearSelection();
            }
        }

        private void FolderButton_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is FolderItemViewModel folderVm && DataContext is LibraryViewModel vm)
            {
                vm.SelectedFolder = folderVm;
            }
        }

        private void DataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _isDraggingInternalScores = false;
        }

        private void DataGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_isDraggingInternalScores && DataContext is LibraryViewModel vm)
            {
                Point currentPos = e.GetPosition(null);
                Vector diff = _dragStartPoint - currentPos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var selected = vm.SelectedScores.ToList();
                    if (selected.Count == 0 && vm.SelectedScore != null)
                    {
                        selected.Add(vm.SelectedScore);
                    }

                    if (selected.Count > 0)
                    {
                        _isDraggingInternalScores = true;
                        var data = new DataObject("RobloxPiano_SelectedScores", selected.Select(s => s.Id).ToArray());
                        DragDrop.DoDragDrop(ScoresDataGrid, data, DragDropEffects.Move);
                        _isDraggingInternalScores = false;
                    }
                }
            }
        }

        private void DataGrid_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
            }
        }

        private async void DataGrid_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && DataContext is LibraryViewModel vm)
            {
                var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    await vm.ImportFilesAsync(files, vm.CurrentFolderId);
                    e.Handled = true;
                }
            }
        }

        private void FolderButton_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent("RobloxPiano_SelectedScores"))
            {
                e.Effects = DragDropEffects.Move | DragDropEffects.Copy;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
            }
        }

        private async void FolderButton_Drop(object sender, DragEventArgs e)
        {
            if (DataContext is not LibraryViewModel vm) return;

            string? targetFolderId = null;
            if (sender is FrameworkElement elem && elem.DataContext is FolderItemViewModel folderVm)
            {
                targetFolderId = folderVm.Id;
            }

            if (e.Data.GetDataPresent("RobloxPiano_SelectedScores"))
            {
                var scoreIds = (string[]?)e.Data.GetData("RobloxPiano_SelectedScores");
                if (scoreIds != null && scoreIds.Length > 0)
                {
                    await vm.MoveScoresToFolderAsync(scoreIds, targetFolderId);
                    e.Handled = true;
                    return;
                }
            }

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    await vm.ImportFilesAsync(files, targetFolderId);
                    e.Handled = true;
                }
            }
        }

        private async void RootFolder_Drop(object sender, DragEventArgs e)
        {
            if (DataContext is not LibraryViewModel vm) return;

            if (e.Data.GetDataPresent("RobloxPiano_SelectedScores"))
            {
                var scoreIds = (string[]?)e.Data.GetData("RobloxPiano_SelectedScores");
                if (scoreIds != null && scoreIds.Length > 0)
                {
                    await vm.MoveScoresToFolderAsync(scoreIds, null);
                    e.Handled = true;
                    return;
                }
            }

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    await vm.ImportFilesAsync(files, null);
                    e.Handled = true;
                }
            }
        }

        private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Focus guard: if focus is within an editable text input, do NOT intercept
            var focusedElement = Keyboard.FocusedElement;
            if (focusedElement is TextBox || focusedElement is PasswordBox || focusedElement is RichTextBox)
            {
                if (e.Key == Key.Escape)
                {
                    if (focusedElement is TextBox tb && tb.Text.Length > 0)
                    {
                        tb.Text = string.Empty;
                        e.Handled = true;
                    }
                }
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
                if (vm.SelectedScores.Count == 1 || (vm.SelectedScores.Count == 0 && vm.SelectedScore != null))
                {
                    vm.OpenSelectedScore();
                    e.Handled = true;
                }
                else if (vm.SelectedFolder != null && !vm.IsFavoritesView && vm.SelectedScores.Count == 0)
                {
                    _ = vm.NavigateToFolderCommand.ExecuteAsync(vm.SelectedFolder.Id);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                ScoresDataGrid.SelectAll();
                e.Handled = true;
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
            else if (e.Key == Key.F5)
            {
                _ = vm.RefreshLibraryCommand.ExecuteAsync(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Back || (e.Key == Key.Left && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt))
            {
                if (vm.CanGoBack)
                {
                    _ = vm.NavigateBackCommand.ExecuteAsync(null);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Right && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                if (vm.CanGoForward)
                {
                    _ = vm.NavigateForwardCommand.ExecuteAsync(null);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Up && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                if (vm.CanGoUp)
                {
                    _ = vm.NavigateUpCommand.ExecuteAsync(null);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape)
            {
                if (vm.HasSearchText)
                {
                    vm.ClearSearch();
                    e.Handled = true;
                }
                else
                {
                    ScoresDataGrid.SelectedItems.Clear();
                    vm.ClearSelection();
                    e.Handled = true;
                }
            }
        }
    }
}
