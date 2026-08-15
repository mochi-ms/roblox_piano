using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace RobloxPiano.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception, "DispatcherUnhandledException");
        ShowCrashDialog(e.Exception);
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogCrash(ex, "AppDomainUnhandledException");
            ShowCrashDialog(ex);
        }
    }

    private static string GetLogPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var logDir = Path.Combine(localAppData, "RobloxPianoPlayer", "Logs");
        Directory.CreateDirectory(logDir);
        return Path.Combine(logDir, "startup-crash.log");
    }

    private static void LogCrash(Exception ex, string source)
    {
        try
        {
            var logPath = GetLogPath();
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Fatal Crash ({source})");
            sb.AppendLine($"Type: {ex.GetType().FullName}");
            sb.AppendLine($"Message: {ex.Message}");
            sb.AppendLine($"StackTrace:\n{ex.StackTrace}");
            if (ex.InnerException != null)
            {
                sb.AppendLine($"InnerType: {ex.InnerException.GetType().FullName}");
                sb.AppendLine($"InnerMessage: {ex.InnerException.Message}");
                sb.AppendLine($"InnerStackTrace:\n{ex.InnerException.StackTrace}");
            }
            sb.AppendLine(new string('-', 60));
            File.AppendAllText(logPath, sb.ToString());
        }
        catch
        {
            // Logging failure must never throw
        }
    }

    private static int _isShowingDialog = 0;

    private static void ShowCrashDialog(Exception ex)
    {
        if (Interlocked.CompareExchange(ref _isShowingDialog, 1, 0) != 0)
        {
            return;
        }

        try
        {
            var logPath = GetLogPath();
            MessageBox.Show(
                $"Roblox Piano 실행 중 예외가 발생했습니다.\n\n" +
                $"오류 내용: {ex.Message}\n" +
                $"로그 파일: {logPath}",
                "Roblox Piano 오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // UI failure must never throw
        }
        finally
        {
            _isShowingDialog = 0;
        }
    }
}

