using System.Reflection;
using RobloxPiano.Core.Piano;
using RobloxPiano.Desktop.ViewModels;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class DesktopStartupTests
{
    [Fact]
    public void Desktop_AllViewModels_HaveParameterlessConstructorsForXamlActivator()
    {
        var viewModelTypes = new[]
        {
            typeof(MainViewModel),
            typeof(PlayerViewModel),
            typeof(LibraryViewModel),
            typeof(ImportViewModel),
            typeof(TranscribeViewModel),
            typeof(SettingsViewModel)
        };

        foreach (var type in viewModelTypes)
        {
            var defaultCtor = type.GetConstructor(
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null
            );

            Assert.True(defaultCtor != null, $"ViewModel '{type.FullName}' must have an explicit public parameterless constructor for WPF XAML activation.");
            
            // Verify Activator can instantiate without throwing MissingMethodException
            var instance = Activator.CreateInstance(type);
            Assert.NotNull(instance);

            if (instance is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    [Fact]
    public void Desktop_MainViewModel_ConstructsWithoutOptionalExternalTools()
    {
        using var mainVm = new MainViewModel();

        Assert.NotNull(mainVm.PlayerViewModel);
        Assert.NotNull(mainVm.LibraryViewModel);
        Assert.NotNull(mainVm.ImportViewModel);
        Assert.NotNull(mainVm.TranscribeViewModel);
        Assert.NotNull(mainVm.SettingsViewModel);
        Assert.NotNull(mainVm.HotkeyService);
        Assert.NotNull(mainVm.ProfileContext);

        Assert.Same(mainVm.PlayerViewModel, mainVm.CurrentView);
    }

    [Fact]
    public void PianoProfileLoader_WorksWhenCurrentDirectoryIsNotRepoRoot()
    {
        var originalCwd = Directory.GetCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "RobloxPiano_CwdTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            Directory.SetCurrentDirectory(tempDir);

            var profile61 = PianoProfileLoader.Load61KeyProfile();
            Assert.NotNull(profile61);
            Assert.Equal(36, profile61.MinPitch);
            Assert.Equal(96, profile61.MaxPitch);
            Assert.NotEmpty(profile61.Keys);

            var profile88 = PianoProfileLoader.Load88KeyProfile();
            Assert.NotNull(profile88);
            Assert.Equal(21, profile88.MinPitch);
            Assert.Equal(108, profile88.MaxPitch);
            Assert.NotEmpty(profile88.Keys);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
