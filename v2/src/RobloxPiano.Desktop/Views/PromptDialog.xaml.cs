using System.Windows;
using System.Windows.Input;

namespace RobloxPiano.Desktop.Views;

public partial class PromptDialog : Window
{
    public string? ResultText { get; private set; }

    public PromptDialog(string title, string message, string defaultText = "")
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        InputBox.Text = defaultText;

        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        };
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        ResultText = InputBox.Text;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        ResultText = null;
        DialogResult = false;
        Close();
    }
}
