namespace RobloxPiano.Desktop.Services;

public interface IUserInteractionService
{
    string? PromptText(string title, string message, string defaultText = "");
    bool Confirm(string title, string message);
    void ShowError(string title, string message);
    void ShowInfo(string title, string message);
}
