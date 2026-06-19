using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Resources;
using LlamaServerLauncher.Services;
using LlamaServerLauncher.ViewModels;

namespace LlamaServerLauncher;

public partial class MainWindow : Window
{
    private MainViewModel? _viewModel;
    private IntPtr _windowHandle;
    private ConfigurationService? _configService;
    private bool _isClosing = false;
    private bool _isAutoFitActive;
    private double _originalMinHeight;
    private bool _isCustomLogDrag;
    private double _customLogDragStartY;
    private double _customLogDragStartHeight;
    private bool _isAutoCompleting;
    private bool _suppressAutoComplete;

    // Width below which the nav pane auto-collapses. Hysteresis: a manual hamburger
    // toggle sticks until the width crosses this threshold again.
    private const double NavAutoCollapseWidth = 880;
    private bool? _navWideState;
    private TextBox? _profileComboBoxTextBox;
    private System.Threading.CancellationTokenSource? _autoStartCts;

    public static MainWindow? Instance { get; private set; }
    public IntPtr WindowHandle => _windowHandle;

    public MainWindow()
    {
        Instance = this;
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        
        // Subscribe to window state changes for minimize-to-tray
        Opened += (s, e) =>
        {
            this.GetValue(Window.WindowStateProperty);
            InitializeProfileComboBoxAutoComplete();
        };
        
        // Use the fact that Window implements IPriorityValue
        // by subscribing to the WindowState property changes via Avalonia's property system
        if (this is AvaloniaObject ao)
        {
            ao.PropertyChanged += (s, e) =>
            {
                if (e.Property == Window.WindowStateProperty && WindowState == WindowState.Minimized)
                {
                    Hide();
                }
            };
        }
        
        _windowHandle = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        
        // Intercept GridSplitter pointer events for custom drag in auto-fit mode
        var splitter = this.FindControl<GridSplitter>("LogGridSplitter");
        if (splitter != null)
        {
            splitter.AddHandler(PointerPressedEvent, OnLogSplitterPointerPressed, RoutingStrategies.Tunnel);
            splitter.AddHandler(PointerMovedEvent, OnLogSplitterPointerMoved, RoutingStrategies.Tunnel);
            splitter.AddHandler(PointerReleasedEvent, OnLogSplitterPointerReleased, RoutingStrategies.Tunnel);
        }
        
        // Collapse/expand the nav pane as the window gets narrower/wider.
        SizeChanged += (s, e) => UpdateNavPaneForWidth();

        // AllowDrop is set in XAML via dd:DragDrop.AllowDrop="True"
        System.Diagnostics.Debug.WriteLine("MainWindow initialized");
    }

    private async void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            _viewModel = vm;
            _configService = new ConfigurationService(_viewModel.LogService, _viewModel.CurrentDataPath);
            vm.PropertyChanged += ViewModel_PropertyChanged;
            vm.OpenDownloadDialogFunc = OpenDownloadDialogAsync;

            vm.ConfirmActionFunc = async (title, message) =>
            {
                var result = await MessageBox.ShowAsync(this, message, title, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                return result == MessageBoxResult.Yes;
            };
            vm.ShowMessageFunc = async (title, message, type) =>
            {
                var icon = type == "error" ? MessageBoxIcon.Error : MessageBoxIcon.Information;
                await MessageBox.ShowAsync(this, message, title, MessageBoxButtons.OK, icon);
            };
            vm.BrowseFolderFunc = async (title) =>
            {
                return await WindowsFileDialogs.OpenFolderDialogAsync(title);
            };

            await vm.InitializeAsync();
            await LoadWindowPositionAsync();

            // Re-evaluate the nav-pane for the restored window width.
            UpdateNavPaneForWidth();

            // Apply auto-fit after initial height is set
            if (_viewModel.AutoFitHeight)
                EnableAutoFitHeight();

            // Auto-start scenario after the window is fully loaded
            _autoStartCts = new System.Threading.CancellationTokenSource();
            var token = _autoStartCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500, token);
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await _viewModel.RunAutoStartScenarioAsync();
                    });
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Auto-start scenario failed: {ex.Message}");
                }
            }, token);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.LogText) && _viewModel?.AutoScroll == true)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var scrollViewer = this.FindControl<ScrollViewer>("LogScrollViewer");
                scrollViewer?.ScrollToEnd();
            });
        }
        else if (e.PropertyName == nameof(MainViewModel.AutoFitHeight))
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_viewModel == null) return;
                if (_viewModel.AutoFitHeight && !_isAutoFitActive)
                    EnableAutoFitHeight();
                else if (!_viewModel.AutoFitHeight && _isAutoFitActive)
                {
                    DisableAutoFitHeight();
                    CheckTabContentHeightWarning();
                }
            });
        }
        else if (e.PropertyName == nameof(MainViewModel.SelectedTabIndex))
        {
            Dispatcher.UIThread.Post(CheckTabContentHeightWarning);
        }
        else if (e.PropertyName == nameof(MainViewModel.LogVisible))
        {
            Dispatcher.UIThread.Post(() =>
            {
                var mainGrid = this.FindControl<Grid>("MainGrid");
                if (mainGrid == null || _viewModel == null || mainGrid.RowDefinitions.Count <= 4) return;

                // When maximized, ApplyLogMaximizedState owns the log row.
                if (_viewModel.IsLogMaximized) return;

                if (_viewModel.LogVisible)
                {
                    var h = _viewModel.LogHeight > 0 ? _viewModel.LogHeight : 200;
                    mainGrid.RowDefinitions[4].Height = new GridLength(h);
                }
                else
                {
                    var currentHeight = mainGrid.RowDefinitions[4].Height;
                    if (currentHeight.IsAbsolute && currentHeight.Value > 0)
                        _viewModel.LogHeight = currentHeight.Value;
                    mainGrid.RowDefinitions[4].Height = new GridLength(0);
                }
            });
        }
        else if (e.PropertyName == nameof(MainViewModel.IsLogMaximized))
        {
            Dispatcher.UIThread.Post(ApplyLogMaximizedState);
        }
    }

    // Toggles a "maximized log" layout: settings panel hidden, control panel moves up,
    // log takes the freed space.
    private void ApplyLogMaximizedState()
    {
        var mainGrid = this.FindControl<Grid>("MainGrid");
        var upperGrid = this.FindControl<Grid>("UpperGrid");
        if (mainGrid == null || upperGrid == null || _viewModel == null) return;
        if (mainGrid.RowDefinitions.Count <= 4 || upperGrid.RowDefinitions.Count < 2) return;

        if (_viewModel.IsLogMaximized)
        {
            // Star-sized row needs a fixed window height, so drop auto-fit first.
            if (_viewModel.AutoFitHeight)
                _viewModel.AutoFitHeight = false;

            // Save the current log height so we can restore it when un-maximizing.
            var logRow = mainGrid.RowDefinitions[4].Height;
            if (logRow.IsAbsolute && logRow.Value > 0)
                _viewModel.LogHeight = logRow.Value;

            upperGrid.RowDefinitions[0].Height = new GridLength(0);                       // collapse settings panel
            mainGrid.RowDefinitions[2].Height = GridLength.Auto;                          // shrink to control panel
            mainGrid.RowDefinitions[4].Height = new GridLength(1, GridUnitType.Star);     // log fills the rest
        }
        else
        {
            upperGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);    // restore settings panel
            // Auto-fit keeps the settings row sized to content; otherwise it fills the window.
            mainGrid.RowDefinitions[2].Height = _viewModel.AutoFitHeight
                ? GridLength.Auto
                : new GridLength(1, GridUnitType.Star);
            var h = _viewModel.LogHeight > 0 ? _viewModel.LogHeight : 200;
            mainGrid.RowDefinitions[4].Height = _viewModel.LogVisible ? new GridLength(h) : new GridLength(0);
        }
    }

    private void ToggleLogMaximizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.IsLogMaximized = !_viewModel.IsLogMaximized;
    }

    // Collapse the nav pane when narrow, expand it when wide. Only reacts when the
    // width actually crosses the threshold, so a manual hamburger toggle within the
    // same band is preserved.
    private void UpdateNavPaneForWidth()
    {
        if (_viewModel == null || _isClosing) return;

        var width = Width;
        if (double.IsNaN(width) || width <= 0) return; // skip bad sizes during layout/teardown

        bool wide = width >= NavAutoCollapseWidth;
        if (_navWideState == null || wide != _navWideState)
        {
            _navWideState = wide;
            _viewModel.IsNavPaneOpen = wide;
        }
    }

    private void EnableAutoFitHeight()
    {
        var mainGrid = this.FindControl<Grid>("MainGrid");
        if (mainGrid == null || _viewModel == null) return;

        // Auto-fit and the maximized log both want to drive the settings row; drop one.
        if (_viewModel.IsLogMaximized)
            _viewModel.IsLogMaximized = false;

        // Save current height as LH before enabling auto-fit
        _viewModel.AutoFitHeightSavedHeight = Height;

        // Change TabControl row (row 3) from * to Auto so content drives height
            if (mainGrid.RowDefinitions.Count > 2)
                mainGrid.RowDefinitions[2].Height = GridLength.Auto;

        _originalMinHeight = MinHeight;
        MinHeight = 0;
        CanResize = false;
        SizeToContent = SizeToContent.Height;
        _isAutoFitActive = true;
    }

    private void DisableAutoFitHeight()
    {
        var mainGrid = this.FindControl<Grid>("MainGrid");

        // Restore manual sizing first, then row definition
        SizeToContent = SizeToContent.Manual;
        MinHeight = _originalMinHeight;
        CanResize = true;

            // Keep the settings row collapsed while the log is maximized, otherwise
            // it becomes a second star row and leaves a gap above the log.
            if (mainGrid != null && mainGrid.RowDefinitions.Count > 2)
                mainGrid.RowDefinitions[2].Height = _viewModel?.IsLogMaximized == true
                    ? GridLength.Auto
                    : new GridLength(1, GridUnitType.Star);

        // Restore height from LH
        if (_viewModel != null)
            Height = _viewModel.AutoFitHeightSavedHeight;

        _isAutoFitActive = false;
    }

    private void CheckTabContentHeightWarning()
    {
        if (_viewModel == null) return;
        if (_viewModel.AutoFitHeight) return;

        // Wait one layout pass so the scroll viewport/extent are up to date.
        Dispatcher.UIThread.Post(() =>
        {
            var tabControl = this.FindControl<TabControl>("MainTabControl");
            if (tabControl == null || _viewModel == null) return;

            // The active tab wraps its content in a ScrollViewer; only warn when
            // the content actually overflows the viewport.
            var scrollViewer = tabControl.SelectedContent as ScrollViewer
                ?? tabControl.GetVisualDescendants()
                    .OfType<ScrollViewer>()
                    .FirstOrDefault(sv => sv.IsEffectivelyVisible && sv.Bounds.Height > 0);

            bool contentClipped = scrollViewer != null
                && scrollViewer.Extent.Height > scrollViewer.Viewport.Height + 1;

            _viewModel.CheckAutoFitHeightWarning(contentClipped);
        });
    }

    private void OnLogSplitterPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isAutoFitActive) return;

        var mainGrid = this.FindControl<Grid>("MainGrid");
        if (mainGrid == null || mainGrid.RowDefinitions.Count <= 4) return;

        _isCustomLogDrag = true;
        _customLogDragStartY = e.GetPosition(this).Y;
        _customLogDragStartHeight = mainGrid.RowDefinitions[4].Height.Value;
        e.Handled = true;
        e.Pointer.Capture((IInputElement)sender!);
    }

    private void OnLogSplitterPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isCustomLogDrag) return;

        var mainGrid = this.FindControl<Grid>("MainGrid");
        if (mainGrid == null || mainGrid.RowDefinitions.Count <= 4) return;

        var currentY = e.GetPosition(this).Y;
        var delta = currentY - _customLogDragStartY;
        var newHeight = Math.Max(50, _customLogDragStartHeight + delta);
        mainGrid.RowDefinitions[4].Height = new GridLength(newHeight);
        e.Handled = true;
    }

    private void OnLogSplitterPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isCustomLogDrag) return;
        _isCustomLogDrag = false;
        e.Handled = true;
    }

    private async Task LoadWindowPositionAsync()
    {
        if (_configService == null || _viewModel == null) return;
        
        var settings = await _configService.LoadAppSettingsAsync();
        
        if (settings.WindowWidth > 0) Width = settings.WindowWidth;
        
        if (settings.AutoFitHeight && settings.AutoFitHeightSavedHeight > 0)
            Height = settings.AutoFitHeightSavedHeight;
        else if (settings.WindowHeight > 0)
            Height = settings.WindowHeight;

        if (settings.WindowLeft.HasValue && settings.WindowTop.HasValue)
        {
            var left = settings.WindowLeft.Value;
            var top = settings.WindowTop.Value;
            
            if (left >= 0 && left < 3000 && top >= 0 && top < 2000)
            {
                Position = new PixelPoint((int)left, (int)top);
            }
        }

        _viewModel.WindowWidth = Width;
        _viewModel.WindowHeight = Height;
        _viewModel.WindowLeft = Position.X;
        _viewModel.WindowTop = Position.Y;

        var mainGrid = this.FindControl<Grid>("MainGrid");
        if (mainGrid != null && mainGrid.RowDefinitions.Count > 4)
        {
            var logHeight = settings.LogHeight > 0 ? settings.LogHeight : 200;
            mainGrid.RowDefinitions[4].Height = new GridLength(logHeight);
            mainGrid.RowDefinitions[4].MinHeight = 50;
        }
    }

    private async Task SaveWindowPositionAsync()
    {
        if (_configService == null || _viewModel == null) return;
        
        var mainGrid = this.FindControl<Grid>("MainGrid");
        if (mainGrid != null && mainGrid.RowDefinitions.Count > 4 && _viewModel.LogVisible)
        {
            var logRow = mainGrid.RowDefinitions[4];
            if (logRow.Height.IsAbsolute && logRow.Height.Value > 0)
                _viewModel.LogHeight = logRow.Height.Value;
        }
        
        _viewModel.WindowWidth = Width;
        
        if (_viewModel.AutoFitHeight)
            _viewModel.WindowHeight = _viewModel.AutoFitHeightSavedHeight;
        else
            _viewModel.WindowHeight = Height;
        
        _viewModel.WindowLeft = Position.X;
        _viewModel.WindowTop = Position.Y;

        await _viewModel.SaveSettingsAsync();
    }

    private void InitializeProfileComboBoxAutoComplete()
    {
        var comboBox = this.FindControl<ComboBox>("ProfileComboBox");
        if (comboBox == null) return;

        comboBox.SelectionChanged += ProfileComboBox_SelectionChanged;
        comboBox.DropDownOpened += ProfileComboBox_DropDownOpened;
        comboBox.AddHandler(PointerWheelChangedEvent, ProfileComboBox_PointerWheelChanged, RoutingStrategies.Tunnel);

        // Try to find TextBox; if not available yet, retry after template is applied
        TrySubscribeTextBox(comboBox);
    }

    private void TrySubscribeTextBox(ComboBox comboBox)
    {
        comboBox.ApplyTemplate();
        _profileComboBoxTextBox = comboBox.FindDescendantOfType<TextBox>();
        if (_profileComboBoxTextBox != null)
        {
            _profileComboBoxTextBox.TextChanged += ProfileComboBox_TextChanged;
            _profileComboBoxTextBox.AddHandler(KeyDownEvent, ProfileComboBoxInnerKeyDown, RoutingStrategies.Tunnel);
        }
    }

    private void ProfileComboBox_DropDownOpened(object? sender, EventArgs e)
    {
        // If TextBox was not found earlier, try again when dropdown opens
        if (_profileComboBoxTextBox == null)
        {
            var comboBox = sender as ComboBox;
            if (comboBox != null)
                TrySubscribeTextBox(comboBox);
        }
    }

    private void ProfileComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null) return;

        var comboBox = sender as ComboBox;
        if (comboBox == null) return;

        // When user selects from dropdown, sync ProfileNameInput with the selected item
        if (comboBox.SelectedItem is string selectedProfile)
        {
            _isAutoCompleting = true;
            try
            {
                if (_profileComboBoxTextBox != null)
                {
                    _profileComboBoxTextBox.Text = selectedProfile;
                    _profileComboBoxTextBox.SelectionStart = selectedProfile.Length;
                    _profileComboBoxTextBox.SelectionEnd = selectedProfile.Length;
                }
                _viewModel.ProfileNameInput = selectedProfile;
            }
            finally
            {
                _isAutoCompleting = false;
            }
        }
    }

    private void ProfileComboBox_TextChanged(object? sender, EventArgs e)
    {
        if (_isAutoCompleting || _viewModel == null || _profileComboBoxTextBox == null)
            return;

        var currentText = _profileComboBoxTextBox.Text ?? "";

        if (string.IsNullOrEmpty(currentText) || _suppressAutoComplete)
        {
            _suppressAutoComplete = false;
            return;
        }

        var profiles = _viewModel.Profiles;
        var match = profiles.FirstOrDefault(p => p.StartsWith(currentText, StringComparison.OrdinalIgnoreCase));

        if (match != null && !string.Equals(match, currentText, StringComparison.OrdinalIgnoreCase))
        {
            _isAutoCompleting = true;
            try
            {
                var caretIndex = currentText.Length;
                _profileComboBoxTextBox.Text = match;
                _profileComboBoxTextBox.SelectionStart = caretIndex;
                _profileComboBoxTextBox.SelectionEnd = match.Length;

                var comboBox = this.FindControl<ComboBox>("ProfileComboBox");
                if (comboBox != null)
                {
                    comboBox.IsDropDownOpen = true;
                    comboBox.SelectedItem = match;
                }
            }
            finally
            {
                _isAutoCompleting = false;
            }
        }
    }

    private void ProfileComboBoxInnerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Back || e.Key == Key.Delete)
        {
            _suppressAutoComplete = true;
        }
    }

    private void ProfileComboBox_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_viewModel == null) return;

        var comboBox = sender as ComboBox;
        if (comboBox == null) return;

        var profiles = _viewModel.Profiles;
        if (profiles.Count == 0) return;

        var currentIndex = -1;
        var selected = comboBox.SelectedItem as string;
        if (selected != null)
            currentIndex = profiles.IndexOf(selected);

        // Scroll up (delta.Y > 0) = previous, scroll down = next
        int direction = e.Delta.Y > 0 ? -1 : 1;
        var newIndex = currentIndex + direction;

        if (newIndex < 0) newIndex = profiles.Count - 1;
        if (newIndex >= profiles.Count) newIndex = 0;

        var newProfile = profiles[newIndex];

        _isAutoCompleting = true;
        try
        {
            comboBox.SelectedItem = newProfile;

            if (_profileComboBoxTextBox != null)
            {
                _profileComboBoxTextBox.Text = newProfile;
                _profileComboBoxTextBox.SelectionStart = newProfile.Length;
                _profileComboBoxTextBox.SelectionEnd = newProfile.Length;
            }

            _viewModel.ProfileNameInput = newProfile;
        }
        finally
        {
            _isAutoCompleting = false;
        }

        e.Handled = true;
    }

    private void ProfileComboBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _viewModel?.LoadProfileCommand.CanExecute(null) == true)
        {
            if (sender is ComboBox cb)
                cb.IsDropDownOpen = false;
            _viewModel.LoadProfileCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void SaveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.SaveProfileCommand.Execute(null);
    }

    private void ResetCustomColorsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.ResetCustomColorsCommand.Execute(null);
    }

    private void LoadClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.LoadProfileCommand.Execute(null);
    }

    private void DeleteClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.DeleteProfileCommand.Execute(null);
    }

    private void RenameClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.RenameProfileCommand.Execute(null);
    }

    private void CloneClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.CloneProfileCommand.Execute(null);
    }

    private void ExportClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.ExportProfileCommand.Execute(null);
    }

    private async void CopyCurrentCommandClick(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel == null) return;
        var text = _viewModel.CurrentCommand;
        if (string.IsNullOrEmpty(text)) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
            await clipboard.SetTextAsync(text);
    }

    private void ImportClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.ImportProfileCommand.Execute(null);
    }

    private void ExportAllClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.ExportAllCommand.Execute(null);
    }

    private void ImportAllClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.ImportAllCommand.Execute(null);
    }

    private void ClearClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.ClearAllFieldsCommand.Execute(null);
    }

    private void ClearExecutablePathClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.ExecutablePath = string.Empty;
    }

    private void ClearModelPathClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.ModelPath = string.Empty;
    }

    private void ClearModelsDirClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.ModelsDir = string.Empty;
    }

    private void ClearContextSizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.ContextSize = string.Empty;
    }

    private void ClearThreadsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.Threads = string.Empty;
    }

    private void ClearTemperatureClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.Temperature = string.Empty;
    }

    private void ClearMaxTokensClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.MaxTokens = string.Empty;
    }

    private void ClearBatchSizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.BatchSize = string.Empty;
    }

    private void ClearUBatchSizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.UBatchSize = string.Empty;
    }

    private void ClearMinPClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.MinP = string.Empty;
    }

    private void ClearTopKClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.TopK = string.Empty;
    }

    private void ClearTopPClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.TopP = string.Empty;
    }

    private void ClearRepeatPenaltyClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.RepeatPenalty = string.Empty;
    }

    private void ClearApiKeyClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.ApiKey = string.Empty;
    }

    private void ClearAliasClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.Alias = string.Empty;
    }

    private void ClearLogFileClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.LogFilePath = string.Empty;
    }

    private void ClearMmprojClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.MmprojPath = string.Empty;
    }

    private void ClearGpuLayersClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.GpuLayers = string.Empty;
    }

    private void ClearCpuMoeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.CpuMoe = string.Empty;
    }

    private void ClearParallelSlotsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.ParallelSlots = string.Empty;
    }

    private void ClearTimeoutClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.Timeout = string.Empty;
    }

    private void ClearReasoningBudgetClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.ReasoningBudget = string.Empty;
    }

    private void ClearSeedClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.Seed = string.Empty;
    }

    private void ClearPresencePenaltyClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.PresencePenalty = string.Empty;
    }

    private void ClearFrequencyPenaltyClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.FrequencyPenalty = string.Empty;
    }

    private void ClearSpecTypeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.SpecType = string.Empty;
    }

    private void ClearSpecDraftNMaxClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.SpecDraftNMax = string.Empty;
    }

    private void ClearSpecDraftNMinClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.SpecDraftNMin = string.Empty;
    }

    private void ClearSpecDraftPSplitClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.SpecDraftPSplit = string.Empty;
    }

    private void ClearSpecDraftPMinClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.SpecDraftPMin = string.Empty;
    }

    private void ClearSpecDraftModelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.SpecDraftModel = string.Empty;
    }

    private void ClearSpecDraftGpuLayersClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.SpecDraftGpuLayers = string.Empty;
    }

    private void ClearHfRepoClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.HfRepo = string.Empty;
    }

    private void ClearHfFileClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.HfFile = string.Empty;
    }

    private void ClearHfRepoDraftClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.HfRepoDraft = string.Empty;
    }

    private void ClearDockerImageClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.DockerImage = "ghcr.io/ggml-org/llama.cpp:server";
    }

    private void ClearDockerContainerNameClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.DockerContainerName = string.Empty;
    }

    private void BrowseDraftModelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.BrowseDraftModelCommand.Execute(null);
    }

    private void ClearCacheTypeKClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.CacheTypeK = string.Empty;
    }

    private void ClearCacheTypeVClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.CacheTypeV = string.Empty;
    }

    private void ClearCustomArgumentsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.ClearCustomArguments();
    }

    private async void OpenArgumentPickerClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        await _viewModel.OpenArgumentPickerAsync();
    }

    private void OpenOptimizerClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.OpenOptimizer();
    }

    private void CustomArgumentToggleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        _viewModel?.RebuildCustomArgumentsFromToggles();
    }

    private void CustomArgumentToggleRightTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is Avalonia.Controls.Primitives.ToggleButton tb && tb.DataContext is CustomArgumentItem item)
        {
            _viewModel?.RemoveCustomArgument(item);
        }
    }

    private void CustomArgumentsTextBox_LostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.ParseAndUpdateCustomArguments();
    }

    private void CustomArgumentsTextBox_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Enter)
        {
            _viewModel?.ParseAndUpdateCustomArguments();
            e.Handled = true;
        }
    }

    private void TriStateCheckBox_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is CheckBox checkBox && checkBox.IsChecked.HasValue)
        {
            // Cycle: false -> true -> null -> false
            if (!checkBox.IsChecked.Value)
                checkBox.IsChecked = true;
            else if (checkBox.IsChecked == true)
                checkBox.IsChecked = null;
            else
                checkBox.IsChecked = false;
            e.Handled = true;
        }
    }

    private void StartServerClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel?.CanStartServer != true) return;
        _viewModel?.DismissServerStartError();
        _viewModel?.StartServerCommand.Execute(null);
    }

    private void RestartServerClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.DismissServerStartError();
        _viewModel?.RestartServerCommand.Execute(null);
    }

    private void StopServerClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.DismissServerStartError();
        _viewModel?.StopServerCommand.Execute(null);
    }

    private void UnloadModelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.DismissServerStartError();
        if (_viewModel?.SelectedInstance?.IsSingleModelMode ?? true)
            return;
        _viewModel?.UnloadModelCommand.Execute(null);
    }

    private void OpenInBrowserClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.DismissServerStartError();
        _viewModel?.OpenInBrowserCommand.Execute(null);
    }

    private void AutoRestartClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.DismissServerStartError();
    }

    private DateTime _lastInstanceClickTime = DateTime.MinValue;

    private async void InstanceButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is SplitButton btn && btn.DataContext is ServerInstance instance)
        {
            var now = DateTime.UtcNow;
            // Double-click detected
            if ((now - _lastInstanceClickTime).TotalMilliseconds < 400
                && instance.IsRunning)
            {
                _lastInstanceClickTime = DateTime.MinValue;
                // Only open browser if web UI is enabled
                if (instance.Configuration.EnableWebUI != false)
                {
                    await instance.OpenInBrowserAsync();
                }
                return;
            }
            _lastInstanceClickTime = now;
            await _viewModel!.SelectInstanceAsync(instance);
        }
    }

    private async void InstanceButtonPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(sender as Avalonia.Visual).Properties.IsRightButtonPressed
            && sender is Avalonia.Input.InputElement control
            && control.DataContext is ServerInstance instance)
        {
            // A failed instance is kept just so the user can still load its profile;
            // right-click should dismiss the error button, not try to stop a process.
            if (!instance.IsRunning && instance.StartFailed)
                _viewModel?.DismissInstance(instance);
            else
                await ConfirmAndStopInstanceAsync(this, instance, _viewModel);
            e.Handled = true;
        }
    }

    public static async Task ConfirmAndStopInstanceAsync(Window parent, ServerInstance instance, MainViewModel? viewModel)
    {
        if (viewModel?.ConfirmStopServer == true)
        {
            var msg = string.Format(LocalizedStrings.GetString("ConfirmStopInstance"), instance.ProfileName);
            var result = await MessageBox.ShowAsync(parent,
                msg,
                LocalizedStrings.Instance.ConfirmTitle,
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);
            if (result != MessageBoxResult.Yes)
                return;
        }
        await instance.StopAsync();
    }

    private ServerInstance? GetInstanceFromMenuItem(object? sender)
    {
        if (sender is MenuItem mi && mi.DataContext is ServerInstance inst)
            return inst;
        return null;
    }

    private async void InstanceStopClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (GetInstanceFromMenuItem(sender) is ServerInstance instance)
            await ConfirmAndStopInstanceAsync(this, instance, _viewModel);
    }

    private async void InstanceRestartClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (GetInstanceFromMenuItem(sender) is ServerInstance instance)
            await instance.RestartAsync();
    }

    private void InstanceAutoRestartClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is MenuItem mi && GetInstanceFromMenuItem(sender) is ServerInstance instance)
        {
            var newVal = !instance.AutoRestart;
            instance.AutoRestart = newVal;
            mi.IsChecked = newVal;
        }
    }

    private void InstanceLogEnabledClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is MenuItem mi && GetInstanceFromMenuItem(sender) is ServerInstance instance)
        {
            var newVal = !instance.LogEnabled;
            instance.LogEnabled = newVal;
            mi.IsChecked = newVal;
        }
    }

    private async void UnloadModelsFlyoutOpened(object? sender, EventArgs e)
    {
        if (sender is not MenuFlyout flyout || _viewModel == null) return;

        try
        {
            flyout.Items.Clear();

            var instance = _viewModel.SelectedInstance;
            if (instance == null || !instance.IsRunning)
            {
                flyout.Items.Add(new MenuItem { Header = _viewModel.Localized.NoLoadedModels, IsEnabled = false });
                return;
            }

            if (instance.IsSingleModelMode)
            {
                flyout.Items.Add(new MenuItem { Header = _viewModel.Localized.NoLoadedModels, IsEnabled = false });
                return;
            }

            var unloadAll = new MenuItem { Header = _viewModel.Localized.UnloadAllModels, Tag = instance };
            unloadAll.Click += UnloadAllForInstanceClick;
            flyout.Items.Add(unloadAll);
            flyout.Items.Add(new Separator());

            var models = await instance.GetLoadedModelsAsync();
            if (models.Count == 0)
            {
                flyout.Items.Add(new MenuItem { Header = _viewModel.Localized.NoLoadedModels, IsEnabled = false });
            }
            else
            {
                foreach (var modelId in models)
                {
                    var item = new MenuItem { Header = modelId, Tag = instance };
                    item.Click += UnloadSingleModelClick;
                    flyout.Items.Add(item);
                }
            }
        }
        catch (Exception ex)
        {
            _viewModel.LogService.Error($"Error populating unload models flyout: {ex.Message}");
        }
    }

    private async void InstanceUnloadSubmenuOpened(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not MenuItem parent || parent.DataContext is not ServerInstance instance || _viewModel == null)
            return;

        try
        {
            if (instance.IsSingleModelMode)
            {
                parent.Items.Clear();
                parent.Items.Add(new MenuItem { Header = _viewModel.Localized.NoLoadedModels, IsEnabled = false });
                return;
            }

            await PopulateUnloadSubmenuAsync(parent, instance);
        }
        catch (Exception ex)
        {
            _viewModel.LogService.Error($"Error populating instance unload submenu: {ex.Message}");
        }
    }

    private async Task PopulateUnloadSubmenuAsync(MenuItem parent, ServerInstance instance)
    {
        parent.Items.Clear();

        var unloadAll = new MenuItem { Header = _viewModel!.Localized.UnloadAllModels, Tag = instance };
        unloadAll.Click += UnloadAllForInstanceClick;
        parent.Items.Add(unloadAll);
        parent.Items.Add(new Separator());

        var models = await instance.GetLoadedModelsAsync();
        if (models.Count == 0)
        {
            parent.Items.Add(new MenuItem { Header = _viewModel.Localized.NoLoadedModels, IsEnabled = false });
        }
        else
        {
            foreach (var modelId in models)
            {
                var item = new MenuItem { Header = modelId, Tag = instance };
                item.Click += UnloadSingleModelClick;
                parent.Items.Add(item);
            }
        }
    }

    private async void UnloadAllForInstanceClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is ServerInstance instance)
            await instance.UnloadModelAsync();
    }

    private async void UnloadSingleModelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is ServerInstance instance && mi.Header is string modelId)
            await instance.UnloadSingleModelAsync(modelId);
    }

    private async void InstanceOpenInBrowserClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (GetInstanceFromMenuItem(sender) is ServerInstance instance)
            await instance.OpenInBrowserAsync();
    }

    private void BrowseBrowserClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.BrowseBrowserCommand.Execute(null);
    }

    private void DismissToastClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Avalonia.Controls.Button btn && btn.DataContext is ToastItem toast)
        {
            _viewModel?.Toasts.Dismiss(toast);
        }
    }

    private void ToastBorderTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is Avalonia.Controls.Border border && border.DataContext is ToastItem toast)
        {
            toast.OnClick?.Invoke();
            _viewModel?.Toasts.Dismiss(toast);
            e.Handled = true;
        }
    }

    private void BrowseExecutableClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.BrowseExecutableCommand.Execute(null);
    }

    private void BrowseModelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.BrowseModelCommand.Execute(null);
    }

    private void BrowseModelsDirClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.BrowseModelsDirCommand.Execute(null);
    }

    private void BrowseLogFileClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.BrowseLogFileCommand.Execute(null);
    }

    private void BrowseMmprojClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.BrowseMmprojCommand.Execute(null);
    }

    private async void LlamaDownloadClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await OpenDownloadDialogAsync();
    }

    private async void UpdateAppClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        await _viewModel.UpdateAppAsync();
    }

    private async void AboutClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dialog = new AboutDialogWindow();
        await dialog.ShowDialog(this);
    }

    private async void UseDefaultDataPathClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        await _viewModel.ToggleDataPathAsync(_viewModel.UseDefaultDataPath);
    }

    private async void DataPathCheckBox_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel == null) return;
        if (e.GetCurrentPoint(sender as Avalonia.Visual).Properties.IsRightButtonPressed)
        {
            if (!_viewModel.UseDefaultDataPath)
            {
                e.Handled = true;
                await _viewModel.ChangeCustomDataPathAsync();
            }
        }
    }

    private void ScenariosEnabledClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
    }

    private void RunScenarioClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.RunScenarioCommand.Execute(null);
    }

    private void EditScenarioClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.EditScenarioCommand.Execute(null);
    }

    private void CreateScenarioClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.CreateScenarioCommand.Execute(null);
    }

    private void DeleteScenarioClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.DeleteScenarioCommand.Execute(null);
    }

    private async void ScenarioAutoStartClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        await _viewModel.ToggleScenarioAutoStartAsync();
    }

    private async Task OpenDownloadDialogAsync()
    {
        if (_viewModel == null) return;
        var vm2 = _viewModel;

        var installDir = vm2.DownloadService.InstallDirectory;
        var affectedInstances = vm2.RunningInstances
            .Where(i => i.IsRunning && IsUsingDefaultExecutable(i, installDir))
            .ToList();
        List<(string ProfileName, ServerConfiguration Configuration)>? wasRunning = null;

        if (affectedInstances.Any())
        {
            var profileList = string.Join("\n", affectedInstances.Select(i => $"• {i.ProfileName}"));
            var message = string.Format(LocalizedStrings.Instance.ConfirmStopForUpdate, profileList);
            var confirm = await MessageBox.ShowAsync(this, message,
                LocalizedStrings.Instance.ConfirmTitle,
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            wasRunning = affectedInstances
                .Select(i => (i.ProfileName, i.Configuration))
                .ToList();
            foreach (var instance in affectedInstances)
            {
                try { await instance.StopAsync(); }
                catch (Exception ex) { vm2.LogService.Error($"Failed to stop instance '{instance.ProfileName}': {ex.Message}"); }
            }
        }

        var dialog = new DownloadDialogWindow();
        var vm = new DownloadDialogViewModel(
            vm2.DownloadService,
            vm2.ReleaseBodyCache,
            vm2.ReleaseBodyCacheOrder,
            vm2.CachedLlamaReleases,
            vm2.CachedLlamaReleasesTimestamp,
            null);
        dialog.SetViewModel(vm, _configService!, vm2.DialogGeometryDict);
        vm.SetExperimentalRepos(vm2.ExperimentalReposEnabled, vm2.ExperimentalRepos);
        await dialog.ShowDialog(this);

        if (dialog.CapturedGeometry != null)
        { vm2.DialogGeometryDict["DownloadDialog"] = dialog.CapturedGeometry; await vm2.SaveSettingsAsync(); }

        if (vm.DownloadSucceeded)
        {
            var downloadedTag = vm.DownloadedReleaseTag;
            if (!string.IsNullOrEmpty(downloadedTag))
                vm2.UpdateInstalledTag(downloadedTag);

            if (!string.IsNullOrEmpty(vm.DownloadedExecutablePath))
            {
                vm2.ExecutablePath = vm.DownloadedExecutablePath;
                if (!string.IsNullOrEmpty(vm.LastCustomDownloadPath))
                    vm2.LlamaCppCustomDownloadPath = vm.LastCustomDownloadPath;
            }
            else
            {
                var defaultPath = vm2.DownloadService.GetDefaultLlamaServerPath();
                if (defaultPath != null && string.IsNullOrEmpty(vm2.ExecutablePath))
                    vm2.ExecutablePath = defaultPath;
            }
        }

        if (wasRunning != null)
        {
            // The binary may have changed (new version), so re-detect supported flags
            // before relaunching. Force bypasses the path-string cache, since an in-place
            // update keeps the same executable path.
            if (vm.DownloadSucceeded)
                await vm2.RefreshSupportedFlagsAsync(force: true);

            foreach (var (profileName, config) in wasRunning)
            {
                try
                {
                    await vm2.LaunchInstanceAsync(profileName, config);
                }
                catch (Exception ex)
                {
                    vm2.LogService.Error($"Failed to restart instance '{profileName}' after update: {ex.Message}");
                }
            }
        }
    }

    private static bool IsUsingDefaultExecutable(ServerInstance instance, string installDir)
    {
        var exePath = instance.Configuration.ExecutablePath;
        if (string.IsNullOrEmpty(exePath)) return true;
        var fullExe = Path.GetFullPath(exePath);
        var fullInstall = Path.GetFullPath(installDir);
        if (!fullInstall.EndsWith(Path.DirectorySeparatorChar))
            fullInstall += Path.DirectorySeparatorChar;
        return fullExe.StartsWith(fullInstall, StringComparison.OrdinalIgnoreCase);
    }

    private async void AddExperimentalRepoClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        var dialogVm = new ViewModels.ExperimentalRepoDialogViewModel(isEditMode: false);
        var dialog = new ExperimentalRepoDialogWindow();
        dialog.SetViewModel(dialogVm, _viewModel.DialogGeometryDict);
        await dialog.ShowDialog(this);

        if (dialog.CapturedGeometry != null)
        { _viewModel.DialogGeometryDict["ExperimentalRepoDialog"] = dialog.CapturedGeometry; await _viewModel.SaveSettingsAsync(); }

        if (dialogVm.Confirmed)
            _viewModel.CreateExperimentalRepoFromDialog(dialogVm.RepoUrl, dialogVm.DisplayName, dialogVm.FilterTags);
    }

    private async void EditExperimentalRepoClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel == null || _viewModel.SelectedExperimentalRepo == null) return;
        var repo = _viewModel.SelectedExperimentalRepo;
        var dialogVm = new ViewModels.ExperimentalRepoDialogViewModel(
            isEditMode: true, repo.RepoUrl, repo.DisplayName, repo.FilterTags);
        var dialog = new ExperimentalRepoDialogWindow();
        dialog.SetViewModel(dialogVm, _viewModel.DialogGeometryDict);
        await dialog.ShowDialog(this);

        if (dialog.CapturedGeometry != null)
        { _viewModel.DialogGeometryDict["ExperimentalRepoDialog"] = dialog.CapturedGeometry; await _viewModel.SaveSettingsAsync(); }

        if (dialogVm.Confirmed)
            _viewModel.UpdateExperimentalRepoFromDialog(repo, dialogVm.RepoUrl, dialogVm.DisplayName, dialogVm.FilterTags);
    }

    private async void DeleteExperimentalRepoClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel == null || _viewModel.SelectedExperimentalRepo == null) return;
        var repo = _viewModel.SelectedExperimentalRepo;
        var msg = string.Format(LocalizedStrings.GetString("ConfirmDeleteRepo"), repo.DisplayName);
        var result = await ViewModels.MessageBox.ShowAsync(this, msg,
            LocalizedStrings.GetString("ConfirmTitle"),
            ViewModels.MessageBoxButtons.YesNoCancel, ViewModels.MessageBoxIcon.Question);
        if (result == ViewModels.MessageBoxResult.Yes)
            _viewModel.DeleteExperimentalRepo(repo);
    }

    private void AddDefaultExperimentalReposClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.AddDefaultExperimentalRepos();
    }

    private void ClearLogClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.ClearLogCommand.Execute(null);
    }

    private void RestartLogStreamClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.RestartLogStreamCommand.Execute(null);
    }

    private async void CopyLogClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        try
        {
            var text = _viewModel.LogText;
            if (string.IsNullOrEmpty(text)) return;
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(text);
        }
        catch
        {
            // Ignore clipboard errors
        }
    }

    private void SaveLogClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.SaveLogCommand.Execute(null);
    }

#region Drag and Drop

    private static string[] SupportedExtensions
    {
        get
        {
            var extensions = new List<string> { ".json", ".bat", ".cmd", ".command", ".sh", ".exe", ".gguf" };
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                extensions.Add(".safetensors");
                extensions.Add(".bin");
                extensions.Add(".mlx");
            }
            return extensions.ToArray();
        }
    }

private void Window_DragEnter(object? sender, Avalonia.Input.DragEventArgs e)
    {
        if (e.DataTransfer.Formats.Contains(Avalonia.Input.DataFormat.File))
        {
            e.DragEffects = Avalonia.Input.DragDropEffects.Copy;
            DragDropOverlay.IsVisible = true;
            return;
        }
        e.DragEffects = Avalonia.Input.DragDropEffects.None;
    }

    private void Window_DragOver(object? sender, Avalonia.Input.DragEventArgs e)
    {
        if (e.DataTransfer.Formats.Contains(Avalonia.Input.DataFormat.File))
        {
            e.DragEffects = Avalonia.Input.DragDropEffects.Copy;
            return;
        }
        e.DragEffects = Avalonia.Input.DragDropEffects.None;
    }

    private void Window_DragLeave(object? sender, Avalonia.Input.DragEventArgs e)
    {
        DragDropOverlay.IsVisible = false;
    }

    private async void Window_Drop(object? sender, Avalonia.Input.DragEventArgs e)
    {
        DragDropOverlay.IsVisible = false;
        
        if (!e.DataTransfer.Formats.Contains(Avalonia.Input.DataFormat.File))
            return;

        var filePath = GetFileFromDataTransfer(e.DataTransfer);
        
        if (string.IsNullOrEmpty(filePath))
        {
            _viewModel?.LogService.Error("Could not determine file path from dropped item");
            return;
        }

        var ext = Path.GetExtension(filePath)?.ToLowerInvariant();
        if (ext == null || !SupportedExtensions.Contains(ext))
            return;

        try
        {
            if (ext == ".json")
            {
                await LoadJsonProfileAsync(filePath);
            }
            else if (ext == ".bat" || ext == ".cmd" || ext == ".command" || ext == ".sh")
            {
                await LoadShellFileAsync(filePath);
            }
            else if (ext == ".exe")
            {
                await HandleDroppedExeAsync(filePath);
            }
            else if (ext == ".gguf" || ext == ".safetensors" || ext == ".bin" || ext == ".mlx")
            {
                HandleDroppedModel(filePath);
            }
        }
        catch (Exception ex)
        {
            _viewModel?.LogService.Error($"Failed to import dropped file: {ex.Message}");
            await MessageBox.ShowAsync(
                this,
                $"Failed to import file: {ex.Message}",
                "Import Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private string? GetFileFromDataTransfer(Avalonia.Input.IDataTransfer dataTransfer)
    {
        try
        {
            var getItemsMethod = dataTransfer.GetType().GetMethod("get_Items");
            if (getItemsMethod != null)
            {
                var result = getItemsMethod.Invoke(dataTransfer, null);
                
                if (result is System.Collections.IEnumerable enumerable)
                {
                    foreach (var item in enumerable)
                    {
                        var dataTransferItem = item as Avalonia.Input.IDataTransferItem;
                        if (dataTransferItem != null)
                        {
                            foreach (var fmt in dataTransferItem.Formats)
                            {
                                var tryGetRawMethod = dataTransferItem.GetType().GetMethod("TryGetRaw");
                                if (tryGetRawMethod != null)
                                {
                                    var invokeResult = tryGetRawMethod.Invoke(dataTransferItem, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public, null, new object[] { fmt }, null);
                                    
                                    if (invokeResult != null)
                                    {
                                        // BclStorageFile has Path property with LocalPath
                                        var pathProp = invokeResult.GetType().GetProperty("Path");
                                        if (pathProp != null)
                                        {
                                            var pathValueObj = pathProp.GetValue(invokeResult);
                                            if (pathValueObj != null)
                                            {
                                                var localPathProp = pathValueObj.GetType().GetProperty("LocalPath");
                                                if (localPathProp != null)
                                                {
                                                    return localPathProp.GetValue(pathValueObj) as string;
                                                }
                                                return pathValueObj.ToString();
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch { }
        
        return null;
    }

    private async Task HandleDroppedExeAsync(string filePath)
    {
        if (_viewModel == null) return;

        var fileName = Path.GetFileName(filePath);
        var isLlamaServer = fileName.Contains("llama-server", StringComparison.OrdinalIgnoreCase);

        string message;
        if (isLlamaServer)
        {
            message = string.Format(LocalizedStrings.Instance.DropExeSet, fileName);
        }
        else
        {
            message = string.Format(LocalizedStrings.Instance.DropExeConfirmMessage, fileName);
        }

        var result = await MessageBox.ShowAsync(
            this,
            message,
            LocalizedStrings.Instance.DropExeConfirmTitle,
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);

        if (result == MessageBoxResult.Yes)
        {
            _viewModel.ExecutablePath = filePath;
            _viewModel.LogService.AppLog(string.Format(LocalizedStrings.Instance.DropExeSetLog, filePath));
        }
    }

    private void HandleDroppedModel(string filePath)
    {
        if (_viewModel == null) return;

        var fileName = Path.GetFileName(filePath);
        if (fileName.Contains("mmproj", StringComparison.OrdinalIgnoreCase))
        {
            _viewModel.MmprojPath = filePath;
            _viewModel.LogService.AppLog(string.Format(LocalizedStrings.Instance.DropMmprojSet, filePath));
        }
        else
        {
            _viewModel.ModelPath = filePath;
            _viewModel.LogService.AppLog(string.Format(LocalizedStrings.Instance.DropModelSet, filePath));
        }
    }

    private async Task LoadJsonProfileAsync(string filePath)
    {
        if (_configService == null || _viewModel == null)
            return;

        var config = await _configService.LoadProfileFromFileAsync(filePath);
        if (config != null)
        {
            _viewModel.LoadConfigFromCommandLine(config);
            _viewModel.LogService.AppLog($"Profile imported from JSON: {filePath}");
        }
        else
        {
            throw new Exception("Failed to parse JSON profile file.");
        }
    }

    private async Task LoadBatFileAsync(string filePath)
    {
        if (_viewModel == null)
            return;

        var content = await File.ReadAllTextAsync(filePath);
        var (exePath, tokens) = ExtractLlamaCommand(content);

        if (tokens.Count == 0)
        {
            _viewModel.LogService.Error($"[Import] No valid llama-server command with model argument (-m, --model, or --models-dir) found in batch file.");
            return;
        }

        var config = ServerConfigurationExtensions.ParseFromTokens(tokens);
        if (config == null)
        {
            _viewModel.LogService.Error($"[Import] Failed to parse command line arguments from batch file.");
            return;
        }

        // Only set exePath if it's a meaningful absolute path (not just ".\" or similar relative)
        if (!string.IsNullOrEmpty(exePath) && 
            exePath != ".\\" && exePath != "./" && 
            exePath != "." && 
            !string.Equals(exePath, ".", StringComparison.OrdinalIgnoreCase))
        {
            config.ExecutablePath = exePath;
        }

        _viewModel.LoadConfigFromCommandLine(config);
        _viewModel.LogService.AppLog($"Profile imported from batch file: {filePath}");
    }

    private async Task LoadShellFileAsync(string filePath)
    {
        if (_viewModel == null)
            return;

        var content = await File.ReadAllTextAsync(filePath);
        var (exePath, tokens) = ExtractLlamaCommandFromShell(content);

        if (tokens.Count == 0)
        {
            _viewModel.LogService.Error($"[Import] No valid llama-server command with model argument (-m, --model, or --models-dir) found in shell script.");
            return;
        }

        var config = ServerConfigurationExtensions.ParseFromTokens(tokens);
        if (config == null)
        {
            _viewModel.LogService.Error($"[Import] Failed to parse command line arguments from shell script.");
            return;
        }

        // Only set exePath if it's a meaningful absolute path (not just "./" or similar relative)
        if (!string.IsNullOrEmpty(exePath) && 
            exePath != "./" && exePath != ".\\" && 
            exePath != "." && 
            !string.Equals(exePath, ".", StringComparison.OrdinalIgnoreCase))
        {
            config.ExecutablePath = exePath;
        }

        _viewModel.LoadConfigFromCommandLine(config);
        _viewModel.LogService.AppLog($"Profile imported from shell script: {filePath}");
    }

    private (string? exePath, List<string> tokens) ExtractLlamaCommand(string batContent)
    {
        var lines = batContent.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
        
        // Use the same parsing that the server uses
        var knownModelArgs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "-m", "--model", "-mu", "--model-url",
            "--models-dir", "--hf-repo", "-hf", "-hfr", "--hf-repo-draft",
            "-hfd", "-hfrd", "--hf-file", "-hff", "--hf-repo-v", "-hfv",
            "--hf-file-v", "--docker-repo", "-dr"
        };

        for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
        {
            var line = lines[lineIdx];
            
            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(line))
                continue;
            
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine))
                continue;
            
            // Skip REM and :: comments
            if (trimmedLine.StartsWith("rem ", StringComparison.OrdinalIgnoreCase) ||
                trimmedLine.StartsWith("rem:", StringComparison.OrdinalIgnoreCase) ||
                trimmedLine.StartsWith("::"))
                continue;

            // Line must contain "llama-server"
            var exeIndex = trimmedLine.IndexOf("llama-server", StringComparison.OrdinalIgnoreCase);
            if (exeIndex < 0)
                continue;

            // Determine exe name end position (check for .exe)
            int exeNameEnd = exeIndex + "llama-server".Length;
            if (exeNameEnd + 4 <= trimmedLine.Length &&
                trimmedLine.Substring(exeNameEnd, 4).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                exeNameEnd += 4;
            }

            // Check if exe is inside quotes (e.g. "C:\llama-server.exe")
            // Scan backwards from exeIndex past path chars to find an opening quote
            string? exePath = null;
            int argsStart;
            int openingQuote = -1;

            for (int qi = exeIndex - 1; qi >= 0; qi--)
            {
                char qc = trimmedLine[qi];
                if (qc == '"' || qc == '\'')
                {
                    openingQuote = qi;
                    break;
                }
                else if (qc == ' ' || qc == '\t')
                {
                    break; // whitespace boundary — no opening quote
                }
                // else: path char (e.g. C, :, \), keep scanning
            }

            if (openingQuote >= 0)
            {
                char quoteChar = trimmedLine[openingQuote];
                // Find closing quote after exe name
                int closeQuote = trimmedLine.IndexOf(quoteChar, exeNameEnd);
                if (closeQuote >= 0)
                {
                    exePath = trimmedLine.Substring(openingQuote + 1, closeQuote - openingQuote - 1);
                    argsStart = closeQuote + 1;
                }
                else
                {
                    argsStart = exeNameEnd;
                }
            }
            else
            {
                // Not quoted — extract path prefix from before exe name
                if (exeIndex > 0)
                {
                    var beforeExe = trimmedLine.Substring(0, exeIndex);
                    var pathStart = 0;
                    while (pathStart < beforeExe.Length && (beforeExe[pathStart] == ' ' || beforeExe[pathStart] == '\t'))
                        pathStart++;

                    if (pathStart < beforeExe.Length)
                    {
                        var potentialPath = beforeExe.Substring(pathStart);
                        potentialPath = potentialPath.TrimEnd(' ', '\t');
                        if (potentialPath.EndsWith("\""))
                            potentialPath = potentialPath.TrimEnd('"');

                        if (!string.IsNullOrWhiteSpace(potentialPath))
                        {
                            exePath = potentialPath;
                        }
                    }
                }
                argsStart = exeNameEnd;
            }

            string afterExe = argsStart < trimmedLine.Length ? trimmedLine.Substring(argsStart) : "";

            // Strip leading quote if the executable name was quoted (e.g. "llama-server")
            if (afterExe.Length > 0 && (afterExe[0] == '"' || afterExe[0] == '\''))
                afterExe = afterExe.Substring(1);

            // Use CommandLineParser.ParseArguments to properly split arguments
            var parsedArgs = CommandLineParser.ParseArguments(afterExe);
            
            // Check if any model-related argument exists with non-empty value
            bool hasModelArg = false;
            for (int i = 0; i < parsedArgs.Count; i++)
            {
                var arg = parsedArgs[i];
                if (knownModelArgs.Contains(arg))
                {
                    // Check if next arg is a value (not another flag)
                    if (i + 1 < parsedArgs.Count && !parsedArgs[i + 1].StartsWith("-"))
                    {
                        hasModelArg = true;
                        break;
                    }
                    // Also check for --models-dir which is a flag itself (no value needed)
                    if (arg == "--models-dir")
                    {
                        hasModelArg = true;
                        break;
                    }
                }
            }
            
            if (!hasModelArg)
                continue;

            // Remove trailing pause/exit commands
            while (parsedArgs.Count > 0 && 
                   (parsedArgs[^1].Equals("pause", StringComparison.OrdinalIgnoreCase) ||
                    parsedArgs[^1].Equals("&", StringComparison.OrdinalIgnoreCase)))
            {
                parsedArgs.RemoveAt(parsedArgs.Count - 1);
            }
            
            if (parsedArgs.Count > 0)
            {
                return (exePath, parsedArgs);
            }
        }

        return (null, new List<string>());
    }

    private (string? exePath, List<string> tokens) ExtractLlamaCommandFromShell(string shellContent)
    {
        var lines = shellContent.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
        
        var knownModelArgs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "-m", "--model", "-mu", "--model-url",
            "--models-dir", "--hf-repo", "-hf", "-hfr", "--hf-repo-draft",
            "-hfd", "-hfrd", "--hf-file", "-hff", "--hf-repo-v", "-hfv",
            "--hf-file-v", "--docker-repo", "-dr"
        };

        for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
        {
            var line = lines[lineIdx];
            
            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(line))
                continue;
            
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine))
                continue;
            
            // Skip shell comments (#) and shebang lines (#!)
            if (trimmedLine.StartsWith("#"))
                continue;

            // Line must contain "llama-server" (exe name or path)
            var exeIndex = trimmedLine.IndexOf("llama-server", StringComparison.OrdinalIgnoreCase);
            if (exeIndex < 0)
                continue;

            // Determine exe name end position (check for .exe)
            int exeNameEnd = exeIndex + "llama-server".Length;
            if (exeNameEnd + 4 <= trimmedLine.Length &&
                trimmedLine.Substring(exeNameEnd, 4).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                exeNameEnd += 4;
            }

            // Check if exe is inside quotes (e.g. "C:\llama-server.exe")
            // Scan backwards from exeIndex past path chars to find an opening quote
            string? exePath = null;
            int argsStart;
            int openingQuote = -1;

            for (int qi = exeIndex - 1; qi >= 0; qi--)
            {
                char qc = trimmedLine[qi];
                if (qc == '"' || qc == '\'')
                {
                    openingQuote = qi;
                    break;
                }
                else if (qc == ' ' || qc == '\t')
                {
                    break; // whitespace boundary — no opening quote
                }
                // else: path char (e.g. C, :, \), keep scanning
            }

            if (openingQuote >= 0)
            {
                char quoteChar = trimmedLine[openingQuote];
                // Find closing quote after exe name
                int closeQuote = trimmedLine.IndexOf(quoteChar, exeNameEnd);
                if (closeQuote >= 0)
                {
                    exePath = trimmedLine.Substring(openingQuote + 1, closeQuote - openingQuote - 1);
                    argsStart = closeQuote + 1;
                }
                else
                {
                    argsStart = exeNameEnd;
                }
            }
            else
            {
                // Not quoted — extract path prefix from before exe name
                if (exeIndex > 0)
                {
                    var beforeExe = trimmedLine.Substring(0, exeIndex);
                    var pathStart = 0;
                    while (pathStart < beforeExe.Length && (beforeExe[pathStart] == ' ' || beforeExe[pathStart] == '\t'))
                        pathStart++;

                    if (pathStart < beforeExe.Length)
                    {
                        var potentialPath = beforeExe.Substring(pathStart);
                        potentialPath = potentialPath.TrimEnd(' ', '\t');
                        if (potentialPath.EndsWith("\""))
                            potentialPath = potentialPath.TrimEnd('"');

                        if (!string.IsNullOrWhiteSpace(potentialPath))
                        {
                            exePath = potentialPath;
                        }
                    }
                }
                argsStart = exeNameEnd;
            }

            string afterExe = argsStart < trimmedLine.Length ? trimmedLine.Substring(argsStart) : "";

            // Strip leading quote if the executable name was quoted (e.g. "llama-server")
            if (afterExe.Length > 0 && (afterExe[0] == '"' || afterExe[0] == '\''))
                afterExe = afterExe.Substring(1);

            // Use CommandLineParser.ParseArguments to properly split arguments
            var parsedArgs = CommandLineParser.ParseArguments(afterExe);
            
            // Check if any model-related argument exists with non-empty value
            bool hasModelArg = false;
            for (int i = 0; i < parsedArgs.Count; i++)
            {
                var arg = parsedArgs[i];
                if (knownModelArgs.Contains(arg))
                {
                    // Check if next arg is a value (not another flag)
                    if (i + 1 < parsedArgs.Count && !parsedArgs[i + 1].StartsWith("-"))
                    {
                        hasModelArg = true;
                        break;
                    }
                    // Also check for --models-dir which is a flag itself (no value needed)
                    if (arg == "--models-dir")
                    {
                        hasModelArg = true;
                        break;
                    }
                }
            }
            
            if (!hasModelArg)
                continue;

            // Remove trailing semicolons and comments (shell style)
            while (parsedArgs.Count > 0)
            {
                var last = parsedArgs[^1];
                if (last.TrimEnd().EndsWith(";") || last.TrimEnd().EndsWith("\\"))
                {
                    parsedArgs.RemoveAt(parsedArgs.Count - 1);
                }
                else
                {
                    break;
                }
            }
            
            if (parsedArgs.Count > 0)
            {
                return (exePath, parsedArgs);
            }
        }

        return (null, new List<string>());
    }

    private static bool ContainsModelArgument(string line)
    {
        // Pattern: argument followed by non-empty value that doesn't start with -
        // Valid: -m model.gguf, --model model.gguf, --models-dir ./models
        // Invalid: -m, --model, -m -other, --models-dir --something

        // Check for -m or --model followed by a value
        if (TryGetArgumentValue(line, "-m", out var val) && !string.IsNullOrWhiteSpace(val) && !val.StartsWith("-"))
            return true;
        if (TryGetArgumentValue(line, "--model", out val) && !string.IsNullOrWhiteSpace(val) && !val.StartsWith("-"))
            return true;
        
        // Check for --models-dir (requires value)
        if (TryGetArgumentValue(line, "--models-dir", out val) && !string.IsNullOrWhiteSpace(val) && !val.StartsWith("-"))
            return true;

        return false;
    }

    private static bool TryGetArgumentValue(string line, string arg, out string value)
    {
        value = string.Empty;
        var argIndex = line.IndexOf(arg, StringComparison.OrdinalIgnoreCase);
        if (argIndex < 0)
            return false;

        // Find the position after the argument
        var valueStart = argIndex + arg.Length;
        if (valueStart > line.Length)
            return false;

        // Skip whitespace
        var wsStart = valueStart;
        while (wsStart < line.Length && (line[wsStart] == ' ' || line[wsStart] == '\t'))
            wsStart++;

        if (wsStart >= line.Length)
            return false;

        // Find the end of the value (next space or end of line, but respect quotes)
        var valueEnd = line.Length; // Default to end of line
        var inQuotes = false;
        char? quoteChar = null;

        for (int i = wsStart; i < line.Length; i++)
        {
            char c = line[i];
            
            if (inQuotes)
            {
                if (c == quoteChar)
                {
                    valueEnd = i; // capture position before closing quote
                    inQuotes = false;
                    quoteChar = null;
                }
                else if (c == '\\' && i + 1 < line.Length && (line[i + 1] == '"' || line[i + 1] == '\\'))
                {
                    i++; // skip escaped quote/backslash
                }
            }
            else if (c == '"' || c == '\'')
            {
                inQuotes = true;
                quoteChar = c;
                valueStart = wsStart + 1; // exclude opening quote
            }
            else if (c == ' ' || c == '\t')
            {
                valueEnd = i; // capture position before the space
                break;
            }
        }

        value = line.Substring(valueStart, valueEnd - valueStart).Trim().Trim('"', '\'');
        return value.Length > 0;
    }

    #endregion

    public bool IsClosingFromTray { get; set; }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        _autoStartCts?.Cancel();
        System.Diagnostics.Debug.WriteLine($"OnClosing: IsClosing={_isClosing}, IsClosingFromTray={IsClosingFromTray}, IsServerRunning={_viewModel?.IsServerRunning}");
        
        if (_isClosing)
        {
            System.Diagnostics.Debug.WriteLine("OnClosing: Already closing, calling base.OnClosing");
            // Already closing - allow it
            base.OnClosing(e);
            return;
        }

        // If closing from tray, skip confirmation dialog and force close
        if (IsClosingFromTray)
        {
            System.Diagnostics.Debug.WriteLine("OnClosing: Closing from tray, stopping server");
            if (_viewModel != null)
            {
                try
                {
                    await _viewModel.StopServerIfRunningAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"OnClosing: Error stopping server: {ex.Message}");
                }
                await SaveWindowPositionAsync();
            }
            _isClosing = true;
            System.Diagnostics.Debug.WriteLine("OnClosing: Calling base.OnClosing");
            _viewModel?.Dispose();
            base.OnClosing(e);
            return;
        }

        if (_viewModel != null && (_viewModel.IsServerRunning || _viewModel.HasAnyRunningInstances))
        {
            // Cancel closing first - we will close manually after dialog
            e.Cancel = true;
            _isClosing = true;
            
            // Server is running - ask for confirmation
            var result = await MessageBox.ShowAsync(
                this,
                LocalizedStrings.Instance.ConfirmCloseMessage,
                LocalizedStrings.Instance.ConfirmCloseTitle,
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (result == MessageBoxResult.Yes)
            {
                // User confirmed - stop server first
                await _viewModel.StopServerIfRunningAsync();
                await SaveWindowPositionAsync();
                
                // Now close for real
                Close();
            }
            else
            {
                // User said No or Cancel - reset flag and cancel
                _isClosing = false;
            }
            // else: user said No or Cancel - window stays open
        }
        else
        {
            // Server isn't running: save settings, then close. Defer the actual close
            // (e.Cancel) so the process can't exit mid-write and corrupt app.json.
            if (_viewModel != null)
            {
                e.Cancel = true;
                _isClosing = true;
                await SaveWindowPositionAsync();
                _viewModel.Dispose();
                Close();
            }
            else
            {
                base.OnClosing(e);
            }
        }
    }
}