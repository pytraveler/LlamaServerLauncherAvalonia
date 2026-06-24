using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LlamaServerLauncher.Resources;

namespace LlamaServerLauncher;

public partial class AboutDialogWindow : Window
{
    public LocalizedStrings Localized => LocalizedStrings.Instance;
    public string AboutTitle => LocalizedStrings.Instance.AboutTitle;
    public string InstalledVersionText => string.Format(LocalizedStrings.Instance.AboutVersion, Models.AppInfo.Version);
    public bool ShowAvailableVersion { get; private set; }
    public string AvailableVersionText { get; private set; } = "";

    public AboutDialogWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    public void SetUpdateState(string? availableTag)
    {
        if (!string.IsNullOrWhiteSpace(availableTag))
        {
            ShowAvailableVersion = true;
            AvailableVersionText = string.Format(LocalizedStrings.Instance.AboutUpdateAvailable, availableTag);
        }
    }

    private void OpenGitHubProfile(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/pytraveler");
    }

    private void OpenAppRepository(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/pytraveler/LlamaServerLauncherAvalonia");
    }

    private void OpenMethelinaProfile(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/Methelina");
    }

    private void OpenLlamaOptimusRepo(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/BrunoArsioli/llama-optimus");
    }

    private void OpenOptunaRepo(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/optuna/optuna");
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    private void CloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
