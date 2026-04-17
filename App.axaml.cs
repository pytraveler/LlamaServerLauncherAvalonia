using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LlamaServerLauncher.Resources;
using LlamaServerLauncher.ViewModels;

namespace LlamaServerLauncher;

public partial class App : Application
{
    private DateTime _lastClickTime = DateTime.MinValue;
    private TrayIcon? _trayIcon;
    private NativeMenu? _trayMenu;
    private MainWindow? _mainWindow;
    private MainViewModel? _viewModel;
    
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
            
            // Use our own RelayCommand that implements ICommand
            var showCmd = new RelayCommand(_ =>
            {
                _mainWindow!.Show();
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Activate();
            });

            var closeCmd = new RelayCommand(async _ =>
            {
                // Check if server is running - if so, show confirmation dialog
                if (_viewModel!.IsServerRunning)
                {
                    // Temporarily show window for dialog
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

                    // Hide window again if it was hidden before
                    if (wasHidden)
                    {
                        _mainWindow.Hide();
                    }

                    if (result != MessageBoxResult.Yes)
                        return; // User cancelled

                    await _viewModel.StopServerIfRunningAsync();
                }

                _mainWindow!.IsClosingFromTray = true;
                _mainWindow.Close();
            });
            
            // Set up tray icon menu via Application.TrayIcon.Icons
            var icons = TrayIcon.GetIcons(this);
            if (icons != null && icons.Count > 0)
            {
                _trayIcon = icons[0];
                
                // Handle Clicked event for double-click detection
                _trayIcon.Clicked += (s, e) =>
                {
                    var now = DateTime.Now;
                    var diff = now - _lastClickTime;
                    _lastClickTime = now;
                    
                    if (diff.TotalMilliseconds < 500)
                    {
                        // Double-click detected - show window
                        _mainWindow!.Show();
                        _mainWindow.WindowState = WindowState.Normal;
                        _mainWindow.Activate();
                    }
                    // Single click - do nothing (menu is shown on right-click)
                };
                
                // Build initial menu
                BuildTrayMenu(closeCmd);
                _trayIcon.Menu = _trayMenu;
                
                // Subscribe to culture changes to rebuild menu
                LocalizedStrings.CultureChanged += OnCultureChanged;
                
                // Subscribe to profile changes to rebuild tray menu
                _viewModel.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(ViewModels.MainViewModel.WindowTitleWithProfile) ||
                        e.PropertyName == nameof(ViewModels.MainViewModel.SelectedProfile) ||
                        e.PropertyName == nameof(ViewModels.MainViewModel.IsServerRunning))
                    {
                        // Rebuild tray menu to update profile names in menu items
                        var closeCmd = new RelayCommand(async _ =>
                        {
                            if (_viewModel!.IsServerRunning)
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

                                await _viewModel.StopServerIfRunningAsync();
                            }

                            _mainWindow!.IsClosingFromTray = true;
                            _mainWindow.Close();
                        });
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
        _trayMenu = new NativeMenu();
        _trayMenu.Add(new NativeMenuItem(LocalizedStrings.Instance.Show) { Command = new CommandAdapter(new RelayCommand(_ =>
        {
            _mainWindow!.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        })) });
        _trayMenu.Add(new NativeMenuItemSeparator());
        
        // Server control commands - include profile name in menu items
        _trayMenu.Add(new NativeMenuItem(GetTrayMenuItemText(LocalizedStrings.Instance.StartServer)) { Command = new CommandAdapter(_viewModel!.StartServerCommand) });
        _trayMenu.Add(new NativeMenuItem(GetTrayMenuItemText(LocalizedStrings.Instance.StopServer)) { Command = new CommandAdapter(_viewModel!.StopServerCommand) });
        _trayMenu.Add(new NativeMenuItem(LocalizedStrings.Instance.UnloadModel) { Command = new CommandAdapter(_viewModel!.UnloadModelCommand) });
        
        _trayMenu.Add(new NativeMenuItemSeparator());
        _trayMenu.Add(new NativeMenuItem(LocalizedStrings.Instance.Close) { Command = new CommandAdapter(closeCmd) });
        
        if (_trayIcon != null)
            _trayIcon.Menu = _trayMenu;
    }

    private void OnCultureChanged()
    {
        if (_trayIcon == null || _viewModel == null) return;
        
        // Rebuild menu with new localized strings
        var closeCmd = new RelayCommand(async _ =>
        {
            if (_viewModel!.IsServerRunning)
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

                await _viewModel.StopServerIfRunningAsync();
            }

            _mainWindow!.IsClosingFromTray = true;
            _mainWindow.Close();
        });
        
        BuildTrayMenu(closeCmd);
    }
}