using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Resources;
using LlamaServerLauncher.Services;
using LlamaServerLauncher.ViewModels;

namespace LlamaServerLauncher;

public partial class App : Application
{
    private DateTime _lastClickTime = DateTime.MinValue;
    private TrayIcon? _trayIcon;
    private NativeMenu? _trayMenu;
    private MainWindow? _mainWindow;
    private MainViewModel? _viewModel;

    public static SingleInstanceService? SingleInstance { get; set; }

    public static void SwitchTheme(string variant, string? colorScheme = null,
                                   Dictionary<string, string>? customColors = null)
    {
        if (Current == null) return;

        Current.RequestedThemeVariant = variant == "Light"
            ? ThemeVariant.Light
            : ThemeVariant.Dark;

        var resources = Current.Resources;

        var themeUri = new Uri($"avares://LlamaServerLauncher/Resources/Themes/{variant}.xaml");
        var themeDict = (ResourceDictionary)AvaloniaXamlLoader.Load(themeUri);

        resources.MergedDictionaries.Clear();

        foreach (string key in themeDict.Keys)
        {
            resources[key] = themeDict[key];
        }

        if (!string.IsNullOrEmpty(colorScheme) && colorScheme != "Default" && colorScheme != "Custom")
        {
            var schemeUri = new Uri($"avares://LlamaServerLauncher/Resources/Themes/Schemes/{colorScheme}.xaml");
            var schemeDict = (ResourceDictionary)AvaloniaXamlLoader.Load(schemeUri);

            foreach (string key in schemeDict.Keys)
            {
                resources[key] = schemeDict[key];
            }
        }

        if (customColors != null)
        {
            foreach (var kv in customColors)
                SetBrush(resources, kv.Key, kv.Value);
        }
    }

    public static void ApplyCustomColor(string key, string hex)
    {
        if (Current == null) return;
        SetBrush(Current.Resources, key, hex);
    }

    private static void SetBrush(IResourceDictionary resources, string key, string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || !Color.TryParse(hex, out var c)) return;
        resources[key] = new SolidColorBrush(c);
    }
    
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _mainWindow = new MainWindow();
            _viewModel = new MainViewModel();
            _mainWindow.DataContext = _viewModel;
            desktop.MainWindow = _mainWindow;

            _mainWindow.Closed += (s, e) => _viewModel?.Cleanup();

            var showCmd = new RelayCommand(_ =>
            {
                _mainWindow!.Show();
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Activate();
            });

            var closeCmd = new RelayCommand(async _ =>
            {
                if (_viewModel!.HasAnyRunningInstances)
                {
                    var wasHidden = !_mainWindow!.IsVisible;
                    if (wasHidden)
                    {
                        _mainWindow.Show();
                        _mainWindow.WindowState = WindowState.Normal;
                    }

                    var result = await MessageBox.ShowAsync(
                        _mainWindow,
                        LocalizedStrings.Instance.ConfirmCloseMessage,
                        LocalizedStrings.Instance.ConfirmCloseTitle,
                        MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Question);

                    if (wasHidden)
                    {
                        _mainWindow.Hide();
                    }

                    if (result != MessageBoxResult.Yes)
                        return;

                    await _viewModel.StopAllInstancesAsync();
                }

                _mainWindow!.IsClosingFromTray = true;
                _mainWindow.Close();
            });

            var icons = TrayIcon.GetIcons(this);
            if (icons != null && icons.Count > 0)
            {
                _trayIcon = icons[0];

                _trayIcon.Clicked += (s, e) =>
                {
                    var now = DateTime.Now;
                    var diff = now - _lastClickTime;
                    _lastClickTime = now;

                    if (diff.TotalMilliseconds < 500)
                    {
                        _mainWindow!.Show();
                        _mainWindow.WindowState = WindowState.Normal;
                        _mainWindow.Activate();
                    }
                };

                BuildTrayMenu(closeCmd);

                LocalizedStrings.CultureChanged += () => BuildTrayMenu(closeCmd);

                _viewModel.RequestTrayMenuRebuild += () => BuildTrayMenu(closeCmd);

                _viewModel.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(ViewModels.MainViewModel.WindowTitleWithProfile) ||
                        e.PropertyName == nameof(ViewModels.MainViewModel.SelectedProfile))
                    {
                        BuildTrayMenu(closeCmd);
                    }
                };
            }

            _mainWindow.PropertyChanged += (s, e) =>
            {
                if (e.Property == Window.WindowStateProperty && _mainWindow!.WindowState == WindowState.Minimized)
                {
                    _mainWindow.Hide();
                }
            };

            _mainWindow.Show();

            if (SingleInstance != null)
            {
                SingleInstance.ActivateRequested += () =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        _mainWindow!.Show();
                        _mainWindow.WindowState = WindowState.Normal;
                        _mainWindow.Activate();
                    });
                };

                if (SingleInstance.ConsumePendingActivation())
                {
                    _mainWindow.Show();
                    _mainWindow.WindowState = WindowState.Normal;
                    _mainWindow.Activate();
                }

                _mainWindow.Closed += (s, e) =>
                {
                    SingleInstance.Dispose();
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private string GetTrayMenuItemText(string baseText)
    {
        var profilePart = string.IsNullOrEmpty(_viewModel!.LoadedProfileName)
            ? (string.IsNullOrEmpty(_viewModel!.SelectedProfile) ? "" : _viewModel!.SelectedProfile)
            : _viewModel!.LoadedProfileName;

        if (string.IsNullOrEmpty(profilePart))
            return baseText;

        return $"{baseText} [{profilePart}]";
    }

    public void BuildTrayMenu(ViewModels.ICommand closeCmd)
    {
        if (_trayMenu == null)
        {
            _trayMenu = new NativeMenu();
        }
        else
        {
            _trayMenu.Items.Clear();
        }

        _trayMenu.Items.Add(new NativeMenuItem(LocalizedStrings.Instance.Show) { Command = new CommandAdapter(new RelayCommand(_ =>
        {
            _mainWindow!.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        })) });
        _trayMenu.Items.Add(new NativeMenuItemSeparator());

        var trayInstances = _viewModel?.RunningInstances.Where(i => i.IsRunning).ToList() ?? new();
        if (trayInstances.Count > 0)
        {
            foreach (var instance in trayInstances)
            {
                var subMenu = new NativeMenu();

                var stopItem = new NativeMenuItem(LocalizedStrings.Instance.StopServer)
                {
                    Command = new CommandAdapter(new AsyncRelayCommand(async () =>
                    {
                        var wasHidden = !_mainWindow!.IsVisible;
                        if (wasHidden)
                        {
                            _mainWindow.Show();
                            _mainWindow.WindowState = WindowState.Normal;
                        }
                        try
                        {
                            await MainWindow.ConfirmAndStopInstanceAsync(_mainWindow, instance, _viewModel);
                        }
                        finally
                        {
                            if (wasHidden) _mainWindow.Hide();
                        }
                    }))
                };
                subMenu.Items.Add(stopItem);

                var restartItem = new NativeMenuItem(LocalizedStrings.Instance.RestartServer)
                {
                    Command = new CommandAdapter(new AsyncRelayCommand(async () => await instance.RestartAsync()))
                };
                subMenu.Items.Add(restartItem);

                subMenu.Items.Add(new NativeMenuItemSeparator());

                var autoRestartText = instance.AutoRestart
                    ? $"✓ {LocalizedStrings.Instance.AutoRestartOnCrash}"
                    : $"  {LocalizedStrings.Instance.AutoRestartOnCrash}";
                var autoRestartItem = new NativeMenuItem(autoRestartText);
                autoRestartItem.Click += (s, e) => instance.AutoRestart = !instance.AutoRestart;
                subMenu.Items.Add(autoRestartItem);

                var logEnabledText = instance.LogEnabled
                    ? $"✓ {LocalizedStrings.Instance.EnableLog}"
                    : $"  {LocalizedStrings.Instance.EnableLog}";
                var logEnabledItem = new NativeMenuItem(logEnabledText);
                logEnabledItem.Click += (s, e) => instance.LogEnabled = !instance.LogEnabled;
                subMenu.Items.Add(logEnabledItem);

                subMenu.Items.Add(new NativeMenuItemSeparator());

                var unloadItem = new NativeMenuItem(LocalizedStrings.Instance.UnloadModel)
                {
                    Command = new CommandAdapter(new AsyncRelayCommand(async () => await instance.UnloadModelAsync()))
                };
                subMenu.Items.Add(unloadItem);

                var openBrowserItem = new NativeMenuItem(LocalizedStrings.Instance.OpenInBrowser)
                {
                    Command = new CommandAdapter(new AsyncRelayCommand(async () => await instance.OpenInBrowserAsync()))
                };
                subMenu.Items.Add(openBrowserItem);

                _trayMenu.Items.Add(new NativeMenuItem(instance.ProfileName) { Menu = subMenu });
            }
        }
        else
        {
            _trayMenu.Items.Add(new NativeMenuItem(LocalizedStrings.Instance.TrayNoServersRunning));
        }

        _trayMenu.Items.Add(new NativeMenuItemSeparator());
        _trayMenu.Items.Add(new NativeMenuItem(LocalizedStrings.Instance.Close) { Command = new CommandAdapter(closeCmd) });

        if (_trayIcon != null)
        {
            if (OperatingSystem.IsMacOS())
            {
                // macOS: reuse the same NativeMenu instance to avoid native proxy mismatch crash
                if (_trayIcon.Menu != _trayMenu)
                    _trayIcon.Menu = _trayMenu;
            }
            else
            {
                // Windows/Linux: force re-assign to refresh native tray menu reliably
                var current = _trayIcon.Menu;
                if (current != _trayMenu)
                    _trayIcon.Menu = _trayMenu;
                else
                {
                    _trayIcon.Menu = null;
                    _trayIcon.Menu = _trayMenu;
                }
            }
        }
    }
}
