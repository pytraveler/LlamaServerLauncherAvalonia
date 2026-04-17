using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
        
        // AllowDrop is set in XAML via dd:DragDrop.AllowDrop="True"
        System.Diagnostics.Debug.WriteLine("MainWindow initialized");
    }

    private async void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            _viewModel = vm;
            _configService = new ConfigurationService(_viewModel.LogService);
            vm.PropertyChanged += ViewModel_PropertyChanged;
            await vm.InitializeAsync();
            await LoadWindowPositionAsync();
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
    }

    private async Task LoadWindowPositionAsync()
    {
        if (_configService == null) return;
        
        var settings = await _configService.LoadAppSettingsAsync();
        
        if (settings.WindowWidth > 0) Width = settings.WindowWidth;
        if (settings.WindowHeight > 0) Height = settings.WindowHeight;

        if (settings.WindowLeft.HasValue && settings.WindowTop.HasValue)
        {
            var left = settings.WindowLeft.Value;
            var top = settings.WindowTop.Value;
            
            // Validate that window is visible on screen
            if (left >= 0 && left < 3000 && top >= 0 && top < 2000)
            {
                Position = new PixelPoint((int)left, (int)top);
            }
        }
    }

    private async Task SaveWindowPositionAsync()
    {
        if (_configService == null || _viewModel == null) return;
        
        var settings = _viewModel.GetAppSettings();
        settings.WindowWidth = Width;
        settings.WindowHeight = Height;
        settings.WindowLeft = Position.X;
        settings.WindowTop = Position.Y;
        await _configService.SaveAppSettingsAsync(settings);
    }

    private void ProfileComboBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _viewModel?.LoadProfileCommand.CanExecute(null) == true)
        {
            _viewModel.LoadProfileCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void SaveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.SaveProfileCommand.Execute(null);
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

    private void ExportClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.ExportProfileCommand.Execute(null);
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
        if (_viewModel != null)
            _viewModel.CustomArguments = string.Empty;
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
        _viewModel?.StartServerCommand.Execute(null);
    }

    private void StopServerClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.StopServerCommand.Execute(null);
    }

    private void UnloadModelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.UnloadModelCommand.Execute(null);
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

    private void ClearLogClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.ClearLogCommand.Execute(null);
    }

#region Drag and Drop

    private static readonly string[] SupportedExtensions = { ".json", ".bat", ".cmd", ".command", ".sh" };

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
        var (exePath, args) = ExtractLlamaCommand(content);

        if (string.IsNullOrWhiteSpace(args))
        {
            _viewModel.LogService.Error($"[Import] No valid llama-server command with model argument (-m, --model, or --models-dir) found in batch file.");
            return;
        }

        var config = ServerConfigurationExtensions.ParseFromCommandLine(args);
        if (config == null)
        {
            _viewModel.LogService.Error($"[Import] Failed to parse command line arguments from: {args}");
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
        var (exePath, args) = ExtractLlamaCommandFromShell(content);

        if (string.IsNullOrWhiteSpace(args))
        {
            _viewModel.LogService.Error($"[Import] No valid llama-server command with model argument (-m, --model, or --models-dir) found in shell script.");
            return;
        }

        var config = ServerConfigurationExtensions.ParseFromCommandLine(args);
        if (config == null)
        {
            _viewModel.LogService.Error($"[Import] Failed to parse command line arguments from: {args}");
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

    private (string? exePath, string args) ExtractLlamaCommand(string batContent)
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

            // Extract all text after llama-server
            string afterExe;
            var exeName = "llama-server.exe";
            if (trimmedLine.IndexOf(exeName, exeIndex, StringComparison.OrdinalIgnoreCase) == exeIndex)
            {
                afterExe = trimmedLine.Substring(exeIndex + exeName.Length);
            }
            else
            {
                // Search for .exe extension after llama-server
                var extSearchStart = exeIndex + "llama-server".Length;
                var exeExtIndex = trimmedLine.IndexOf(".exe", extSearchStart, StringComparison.OrdinalIgnoreCase);
                if (exeExtIndex >= 0)
                {
                    afterExe = trimmedLine.Substring(exeExtIndex + 4);
                }
                else
                {
                    afterExe = trimmedLine.Substring(exeIndex + "llama-server".Length);
                }
            }

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

            // Extract executable path - look BEFORE the "llama-server" in the ORIGINAL line
            string? exePath = null;
            if (exeIndex > 0)
            {
                // Get everything before llama-server
                var beforeExe = trimmedLine.Substring(0, exeIndex);
                
                // Find where the path actually starts (skip leading spaces and quotes)
                var pathStart = 0;
                while (pathStart < beforeExe.Length && (beforeExe[pathStart] == ' ' || beforeExe[pathStart] == '\t'))
                    pathStart++;
                
                if (pathStart < beforeExe.Length)
                {
                    var potentialPath = beforeExe.Substring(pathStart);
                    
                    // Clean up trailing whitespace and quotes
                    potentialPath = potentialPath.TrimEnd(' ', '\t');
                    
                    // Also clean up trailing quote if present (for cases like ".\llama-server.exe")
                    if (potentialPath.EndsWith("\""))
                        potentialPath = potentialPath.TrimEnd('"');
                    
                    if (!string.IsNullOrWhiteSpace(potentialPath))
                    {
                        exePath = potentialPath;
                    }
                }
            }

            // Remove trailing pause/exit commands
            while (parsedArgs.Count > 0 && 
                   (parsedArgs[^1].Equals("pause", StringComparison.OrdinalIgnoreCase) ||
                    parsedArgs[^1].Equals("&", StringComparison.OrdinalIgnoreCase)))
            {
                parsedArgs.RemoveAt(parsedArgs.Count - 1);
            }
            
            // Reconstruct args string (unescape paths)
            var argsList = new List<string>();
            for (int i = 0; i < parsedArgs.Count; i++)
            {
                var arg = parsedArgs[i];
                // Unescape path separators for arguments that typically contain paths
                if (arg.Contains('\\') || arg.Contains("//"))
                {
                    arg = CommandLineBuilder.UnescapePath(arg);
                }
                argsList.Add(arg);
            }
            
            var argsStr = string.Join(" ", argsList);
            
            if (!string.IsNullOrWhiteSpace(argsStr))
            {
                return (exePath, argsStr);
            }
        }

        return (null, string.Empty);
    }

    private (string? exePath, string args) ExtractLlamaCommandFromShell(string shellContent)
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
            
            // Skip shell comments (#)
            if (trimmedLine.StartsWith("#"))
                continue;

            // Skip shebang lines
            if (trimmedLine.StartsWith("#!"))
                continue;

            // Line must contain "llama-server" (exe name or path)
            var exeIndex = trimmedLine.IndexOf("llama-server", StringComparison.OrdinalIgnoreCase);
            if (exeIndex < 0)
                continue;

            // Extract all text after llama-server
            string afterExe;
            var exeName = "llama-server.exe";
            if (trimmedLine.IndexOf(exeName, exeIndex, StringComparison.OrdinalIgnoreCase) == exeIndex)
            {
                afterExe = trimmedLine.Substring(exeIndex + exeName.Length);
            }
            else
            {
                // Search for .exe extension after llama-server (for cross-platform compatibility)
                var extSearchStart = exeIndex + "llama-server".Length;
                var exeExtIndex = trimmedLine.IndexOf(".exe", extSearchStart, StringComparison.OrdinalIgnoreCase);
                if (exeExtIndex >= 0)
                {
                    afterExe = trimmedLine.Substring(exeExtIndex + 4);
                }
                else
                {
                    afterExe = trimmedLine.Substring(exeIndex + "llama-server".Length);
                }
            }

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

            // Extract executable path - look BEFORE the "llama-server" in the ORIGINAL line
            string? exePath = null;
            if (exeIndex > 0)
            {
                // Get everything before llama-server
                var beforeExe = trimmedLine.Substring(0, exeIndex);
                
                // Find where the path actually starts (skip leading spaces and quotes)
                var pathStart = 0;
                while (pathStart < beforeExe.Length && (beforeExe[pathStart] == ' ' || beforeExe[pathStart] == '\t'))
                    pathStart++;
                
                if (pathStart < beforeExe.Length)
                {
                    var potentialPath = beforeExe.Substring(pathStart);
                    
                    // Clean up trailing whitespace and quotes
                    potentialPath = potentialPath.TrimEnd(' ', '\t');
                    
                    // Also clean up trailing quote if present
                    if (potentialPath.EndsWith("\""))
                        potentialPath = potentialPath.TrimEnd('"');
                    
                    if (!string.IsNullOrWhiteSpace(potentialPath))
                    {
                        exePath = potentialPath;
                    }
                }
            }

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
            
            // Reconstruct args string (unescape paths)
            var argsList = new List<string>();
            for (int i = 0; i < parsedArgs.Count; i++)
            {
                var arg = parsedArgs[i];
                // Unescape path separators for arguments that typically contain paths
                if (arg.Contains('\\') || arg.Contains("//"))
                {
                    arg = CommandLineBuilder.UnescapePath(arg);
                }
                argsList.Add(arg);
            }
            
            var argsStr = string.Join(" ", argsList);
            
            if (!string.IsNullOrWhiteSpace(argsStr))
            {
                return (exePath, argsStr);
            }
        }

        return (null, string.Empty);
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
            base.OnClosing(e);
            return;
        }

        if (_viewModel != null && _viewModel.IsServerRunning)
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
            // Server not running - just save settings
            if (_viewModel != null)
            {
                await SaveWindowPositionAsync();
            }
            base.OnClosing(e);
        }
    }
}