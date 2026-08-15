using System.Windows;
using RobloxPiano.Desktop.Views;

namespace RobloxPiano.Desktop.Services;

public class WpfUserInteractionService : IUserInteractionService
{
    public string? PromptText(string title, string message, string defaultText = "")
    {
        var owner = Application.Current?.MainWindow;
        var dialog = new PromptDialog(title, message, defaultText)
        {
            Owner = (owner != null && owner.IsVisible) ? owner : null
        };

        return dialog.ShowDialog() == true ? dialog.ResultText : null;
    }

    public bool Confirm(string title, string message)
    {
        var owner = Application.Current?.MainWindow;
        var result = (owner != null && owner.IsVisible)
            ? MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            : MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);

        return result == MessageBoxResult.Yes;
    }

    public void ShowError(string title, string message)
    {
        var owner = Application.Current?.MainWindow;
        if (owner != null && owner.IsVisible)
        {
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void ShowInfo(string title, string message)
    {
        var owner = Application.Current?.MainWindow;
        if (owner != null && owner.IsVisible)
        {
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
