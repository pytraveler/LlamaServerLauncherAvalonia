using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Resources;
using LlamaServerLauncher.Services;

namespace LlamaServerLauncher.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly LlamaServerService _serverService;
    private readonly ConfigurationService _configService;
    private readonly LogService _logService;
    private ServerConfiguration? _loadedProfileConfig;
    private string _loadedProfileName = string.Empty;

    public LogService LogService => _logService;
    public LocalizedStrings Localized { get; } = LocalizedStrings.Instance;
    
    // Expose _loadedProfileName for tray menu updates
    public string LoadedProfileName => _loadedProfileName;

    private string _selectedLanguage = "en";
    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (_selectedLanguage != value && value != null)
            {
                _selectedLanguage = value;
                OnPropertyChanged();
                ChangeLanguage(value);
            }
        }
    }

    public List<LanguageOption> AvailableLanguages { get; } = new()
    {
        new LanguageOption { Code = "en", Name = "English" },
        new LanguageOption { Code = "ru", Name = "Русский" }
    };

    public List<string> CacheTypes { get; } = new() { "", "f32", "f16", "bf16", "q8_0", "q4_0", "q4_1", "iq4_nl", "q5_0", "q5_1" };

    public record FontSizeOption(string Label, string Value, double Size);
    public List<FontSizeOption> FontSizeOptions { get; } = new()
    {
        new("S", "Small", 12),
        new("M", "Medium", 14),
        new("L", "Large", 16),
        new("XL", "ExtraLarge", 18)
    };

    private string _fontSizeLevel = "Medium";
    public string FontSizeLevel
    {
        get => _fontSizeLevel;
        set
        {
            if (_fontSizeLevel != value && value != null)
            {
                _fontSizeLevel = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedFontSizeOption));
                OnPropertyChanged(nameof(ContentFontSize));
            }
        }
    }

    public FontSizeOption? SelectedFontSizeOption
    {
        get => FontSizeOptions.FirstOrDefault(o => o.Value == _fontSizeLevel);
        set
        {
            if (value != null)
                FontSizeLevel = value.Value;
        }
    }

    public double ContentFontSize => FontSizeOptions.FirstOrDefault(o => o.Value == _fontSizeLevel)?.Size ?? 14;

    private void ChangeLanguage(string? languageCode)
    {
        if (string.IsNullOrEmpty(languageCode)) return;
        var culture = new CultureInfo(languageCode);
        LocalizedStrings.SetCulture(culture);
        OnPropertyChanged(nameof(Localized));
        OnPropertyChanged(nameof(ToggleLogButtonText));
        OnPropertyChanged(nameof(ToggleTabPanelButtonText));
    }

    private string _executablePath = string.Empty;
    private string _modelPath = string.Empty;
    private string _modelsDir = string.Empty;
    private string _host = "127.0.0.1";
    private int _port = 8080;
    private string _contextSize = string.Empty;
    private string _threads = string.Empty;
    private string _gpuLayers = string.Empty;
    private string _temperature = string.Empty;
    private string _maxTokens = string.Empty;
    private string _batchSize = string.Empty;
    private string _uBatchSize = string.Empty;
    private string _minP = string.Empty;
    private string _mmprojPath = string.Empty;
    private string _cacheTypeK = string.Empty;
    private string _cacheTypeV = string.Empty;
    private string _topK = string.Empty;
    private string _topP = string.Empty;
    private string _repeatPenalty = string.Empty;
    private bool? _flashAttention;
    private bool? _enableWebUI;
    private bool? _embeddingMode;
    private bool? _enableSlots;
    private bool? _enableMetrics;
    private string _apiKey = string.Empty;
    private string _logFilePath = string.Empty;
    private bool _verboseLogging;
    private string _alias = string.Empty;
    private string _customArguments = string.Empty;
    private bool _autoRestart;
    private bool _autoScroll = true;
    private bool _logEnabled = true;
    private bool _logVisible = true;
    private bool _tabPanelVisible = true;
    private bool _autoFitHeight;
    private double _autoFitHeightSavedHeight = 650;
    private string _selectedProfile = string.Empty;
    private string _profileNameInput = string.Empty;
    private bool _isServerRunning;
    private string _serverStatus = "Stopped";
    private string _currentLog = string.Empty;
    private string _logOutput = string.Empty;
    private string _logText = string.Empty;
    private string _currentCommand = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel()
    {
        _logService = new LogService();
        _logService.LogReceived += OnLogReceived;
        _serverService = new LlamaServerService(_logService);
        _configService = new ConfigurationService(_logService);

        _serverService.OutputReceived += OnServerOutput;
        _serverService.ServerStateChanged += OnServerStateChanged;

        Profiles = new ObservableCollection<string>();

        ChangeLanguage(_selectedLanguage);

        BrowseExecutableCommand = new AsyncRelayCommand(BrowseExecutableAsync);
        BrowseModelCommand = new AsyncRelayCommand(BrowseModelAsync);
        BrowseModelsDirCommand = new AsyncRelayCommand(BrowseModelsDirAsync);
        BrowseLogFileCommand = new AsyncRelayCommand(BrowseLogFileAsync);
        BrowseMmprojCommand = new AsyncRelayCommand(BrowseMmprojAsync);
        StartServerCommand = new AsyncRelayCommand(StartServerAsync, () => CanStartServer);
        StopServerCommand = new AsyncRelayCommand(StopServerAsync, () => IsServerRunning);
        UnloadModelCommand = new AsyncRelayCommand(UnloadModelAsync, () => IsServerRunning && !_serverService.IsSingleModelMode);
        SaveProfileCommand = new AsyncRelayCommand(SaveProfileAsync);
        LoadProfileCommand = new AsyncRelayCommand(LoadProfileAsync);
        DeleteProfileCommand = new AsyncRelayCommand(DeleteProfileAsync);
        RenameProfileCommand = new AsyncRelayCommand(RenameProfileAsync);
        ExportProfileCommand = new AsyncRelayCommand(ExportProfileAsync);
        ExportToBatCommand = new AsyncRelayCommand(ExportToBatAsync);
        ImportProfileCommand = new AsyncRelayCommand(ImportProfileAsync);
        ExportAllCommand = new AsyncRelayCommand(ExportAllProfilesAsync);
        ImportAllCommand = new AsyncRelayCommand(ImportAllProfilesAsync);
        ClearAllFieldsCommand = new RelayCommand(ClearAllFields);
        ClearLogCommand = new RelayCommand(ClearLog);
        ShowWindowCommand = new RelayCommand(_ => { });
        CloseFromTrayCommand = new RelayCommand(async _ => { });

        LoadProfiles();
        _logService.AppLog("Application started");
        UpdateCurrentCommand();
    }

    public async Task InitializeAsync()
    {
        var settings = await _configService.LoadAppSettingsAsync();
        ApplyAppSettings(settings);
    }

    public void ApplyAppSettings(AppSettings settings)
    {
        var language = string.IsNullOrEmpty(settings.Language) ? "en" : settings.Language;
        
        _selectedLanguage = language;
        ChangeLanguage(language);
        OnPropertyChanged(nameof(SelectedLanguage));
        
        ProfileNameInput = settings.ProfileNameInput;
        ExecutablePath = settings.ExecutablePath;
        ModelPath = settings.ModelPath;
        ModelsDir = settings.ModelsDir;
        Host = settings.Host;
        Port = settings.Port;
        ContextSize = settings.ContextSize;
        Threads = settings.Threads;
        GpuLayers = settings.GpuLayers;
        Temperature = settings.Temperature;
        MaxTokens = settings.MaxTokens;
        BatchSize = settings.BatchSize;
        UBatchSize = settings.UBatchSize;
        MinP = settings.MinP;
        MmprojPath = settings.MmprojPath;
        CacheTypeK = settings.CacheTypeK;
        CacheTypeV = settings.CacheTypeV;
        TopK = settings.TopK;
        TopP = settings.TopP;
        RepeatPenalty = settings.RepeatPenalty;
        FlashAttention = settings.FlashAttention;
        EnableWebUI = settings.EnableWebUI;
        EmbeddingMode = settings.EmbeddingMode;
        EnableSlots = settings.EnableSlots;
        EnableMetrics = settings.EnableMetrics;
        ApiKey = settings.ApiKey;
        LogFilePath = settings.LogFilePath;
        VerboseLogging = settings.VerboseLogging;
        Alias = settings.Alias;
        CustomArguments = settings.CustomArguments;
        AutoRestart = settings.AutoRestart;
        AutoScroll = settings.AutoScrollLog;
        LogEnabled = settings.LogEnabled;
        LogVisible = settings.LogVisible;
        TabPanelVisible = settings.TabPanelVisible;
        AutoFitHeight = settings.AutoFitHeight;
        AutoFitHeightSavedHeight = settings.AutoFitHeightSavedHeight > 0 ? settings.AutoFitHeightSavedHeight : 650;
        FontSizeLevel = string.IsNullOrEmpty(settings.FontSizeLevel) ? "Medium" : settings.FontSizeLevel;
        ParseCustomArguments();
        if (settings.CustomArgumentToggleStates != null && settings.CustomArgumentToggleStates.Count > 0)
        {
            foreach (var item in CustomArgumentItems)
            {
                if (settings.CustomArgumentToggleStates.TryGetValue(item.Name, out var enabled))
                    item.IsEnabled = enabled;
            }
            RebuildCustomArgumentsFromToggles();
        }
    }

    public AppSettings GetAppSettings()
    {
        return new AppSettings
        {
            Language = SelectedLanguage,
            ProfileNameInput = ProfileNameInput,
            ExecutablePath = ExecutablePath,
            ModelPath = ModelPath,
            ModelsDir = ModelsDir,
            Host = Host,
            Port = Port,
            ContextSize = ContextSize,
            Threads = Threads,
            GpuLayers = GpuLayers,
            Temperature = Temperature,
            MaxTokens = MaxTokens,
            BatchSize = BatchSize,
            UBatchSize = UBatchSize,
            MinP = MinP,
            MmprojPath = MmprojPath,
            CacheTypeK = CacheTypeK,
            CacheTypeV = CacheTypeV,
            TopK = TopK,
            TopP = TopP,
            RepeatPenalty = RepeatPenalty,
            FlashAttention = FlashAttention,
            EnableWebUI = EnableWebUI,
            EmbeddingMode = EmbeddingMode,
            EnableSlots = EnableSlots,
            EnableMetrics = EnableMetrics,
            ApiKey = ApiKey,
            LogFilePath = LogFilePath,
            VerboseLogging = VerboseLogging,
            Alias = Alias,
            CustomArguments = string.Join(" ", CustomArgumentItems.Select(x => x.OriginalArg)),
            AutoRestart = AutoRestart,
            AutoScrollLog = AutoScroll,
            LogEnabled = LogEnabled,
            LogVisible = LogVisible,
            TabPanelVisible = TabPanelVisible,
            AutoFitHeight = AutoFitHeight,
            AutoFitHeightSavedHeight = AutoFitHeightSavedHeight,
            FontSizeLevel = FontSizeLevel,
            CustomArgumentToggleStates = GetToggleStates()
        };
    }

    public string ExecutablePath
    {
        get => _executablePath;
        set
        {
            if (_executablePath != value)
            {
                _executablePath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanStartServer));
                OnPropertyChanged(nameof(HasUnsavedChanges));
                UpdateCurrentCommand();
                // Notify StartServerCommand that CanStartServer may have changed
                if (StartServerCommand is AsyncRelayCommand startCmd)
                    startCmd.RaiseCanExecuteChanged();
            }
        }
    }

    public string ModelPath
    {
        get => _modelPath;
        set
        {
            if (_modelPath != value)
            {
                _modelPath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanStartServer));
                OnPropertyChanged(nameof(HasUnsavedChanges));
                UpdateCurrentCommand();
                // Notify StartServerCommand that CanStartServer may have changed
                if (StartServerCommand is AsyncRelayCommand startCmd)
                    startCmd.RaiseCanExecuteChanged();
            }
        }
    }

    public string ModelsDir
    {
        get => _modelsDir;
        set
        {
            if (_modelsDir != value)
            {
                _modelsDir = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanStartServer));
                OnPropertyChanged(nameof(HasUnsavedChanges));
                UpdateCurrentCommand();
                // Notify StartServerCommand that CanStartServer may have changed
                if (StartServerCommand is AsyncRelayCommand startCmd)
                    startCmd.RaiseCanExecuteChanged();
            }
        }
    }

    public string Host
    {
        get => _host;
        set { _host = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public int Port
    {
        get => _port;
        set { _port = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public string ContextSize
    {
        get => _contextSize;
        set { _contextSize = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public string Threads
    {
        get => _threads;
        set { _threads = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public string GpuLayers
    {
        get => _gpuLayers;
        set { _gpuLayers = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public string Temperature
    {
        get => _temperature;
        set { _temperature = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public string MaxTokens
    {
        get => _maxTokens;
        set { _maxTokens = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public string BatchSize
    {
        get => _batchSize;
        set { _batchSize = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public string UBatchSize
    {
        get => _uBatchSize;
        set { _uBatchSize = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public string MinP
    {
        get => _minP;
        set { _minP = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public string MmprojPath
    {
        get => _mmprojPath;
        set { _mmprojPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public string CacheTypeK
    {
        get => _cacheTypeK;
        set { _cacheTypeK = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public string CacheTypeV
    {
        get => _cacheTypeV;
        set { _cacheTypeV = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public string TopK
    {
        get => _topK;
        set { _topK = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public string TopP
    {
        get => _topP;
        set { _topP = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public string RepeatPenalty
    {
        get => _repeatPenalty;
        set { _repeatPenalty = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public bool? FlashAttention
    {
        get => _flashAttention;
        set { _flashAttention = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public bool? EnableWebUI
    {
        get => _enableWebUI;
        set { _enableWebUI = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public bool? EmbeddingMode
    {
        get => _embeddingMode;
        set { _embeddingMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public bool? EnableSlots
    {
        get => _enableSlots;
        set { _enableSlots = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public bool? EnableMetrics
    {
        get => _enableMetrics;
        set { _enableMetrics = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public string ApiKey
    {
        get => _apiKey;
        set { _apiKey = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public string LogFilePath
    {
        get => _logFilePath;
        set { _logFilePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public bool VerboseLogging
    {
        get => _verboseLogging;
        set { _verboseLogging = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public string Alias
    {
        get => _alias;
        set { _alias = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public bool AutoRestart
    {
        get => _autoRestart;
        set { _autoRestart = value; OnPropertyChanged(); }
    }

    public bool AutoScroll
    {
        get => _autoScroll;
        set { _autoScroll = value; OnPropertyChanged(); }
    }

    public bool LogEnabled
    {
        get => _logEnabled;
        set { _logEnabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(ToggleLogButtonText)); }
    }

    public bool LogVisible
    {
        get => _logVisible;
        set { _logVisible = value; OnPropertyChanged(); OnPropertyChanged(nameof(ToggleLogButtonText)); }
    }

    public bool TabPanelVisible
    {
        get => _tabPanelVisible;
        set { _tabPanelVisible = value; OnPropertyChanged(); OnPropertyChanged(nameof(ToggleTabPanelButtonText)); }
    }

    public bool AutoFitHeight
    {
        get => _autoFitHeight;
        set { _autoFitHeight = value; OnPropertyChanged(); }
    }

    public double AutoFitHeightSavedHeight
    {
        get => _autoFitHeightSavedHeight;
        set { _autoFitHeightSavedHeight = value; OnPropertyChanged(); }
    }

    public string ToggleLogButtonText => _logVisible
        ? LocalizedStrings.Instance.HideLog
        : LocalizedStrings.Instance.ShowLog;

    public string ToggleTabPanelButtonText => _tabPanelVisible
        ? LocalizedStrings.Instance.HideTabPanel
        : LocalizedStrings.Instance.ShowTabPanel;

    public string CustomArguments
    {
        get => _customArguments;
        set
        {
            if (_customArguments != value)
            {
                _customArguments = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasUnsavedChanges));
                UpdateCurrentCommand();
                if (!_isUpdatingCustomArguments)
                    UpdateCustomArgumentTogglesFromText();
            }
        }
    }

    public ObservableCollection<CustomArgumentItem> CustomArgumentItems { get; private set; } = new();

    private string _originalCustomArguments = string.Empty;
    private Dictionary<string, bool> _disabledArguments = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    private bool _isUpdatingCustomArguments;

    private string _windowTitle = string.Empty;
    
    public string WindowTitleWithProfile
    {
        get
        {
            var profilePart = string.IsNullOrEmpty(_loadedProfileName) 
                ? (string.IsNullOrEmpty(SelectedProfile) ? "" : SelectedProfile)
                : _loadedProfileName;
            
            var title = string.IsNullOrEmpty(profilePart) 
                ? Localized.WindowTitle 
                : $"{Localized.WindowTitle} [{profilePart}]";
            
            if (IsServerRunning)
            {
                title += " - RUNNING";
            }
            
            return title;
        }
    }

    public string SelectedProfile
    {
        get => _selectedProfile;
        set { _selectedProfile = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); OnPropertyChanged(nameof(WindowTitleWithProfile)); }
    }

    public bool HasUnsavedChanges
    {
        get
        {
            if (_loadedProfileConfig == null || string.IsNullOrEmpty(_loadedProfileName))
                return false;
            
            var currentConfig = GetCurrentConfig();
            return !ConfigsEqual(_loadedProfileConfig, currentConfig);
        }
    }

    private static bool ConfigsEqual(ServerConfiguration a, ServerConfiguration b)
    {
        return a.ExecutablePath == b.ExecutablePath &&
                a.ModelPath == b.ModelPath &&
                a.ModelsDir == b.ModelsDir &&
                a.Host == b.Host &&
                a.Port == b.Port &&
                a.ContextSize == b.ContextSize &&
                a.Threads == b.Threads &&
                a.GpuLayers == b.GpuLayers &&
                a.Temperature == b.Temperature &&
                a.MaxTokens == b.MaxTokens &&
                a.BatchSize == b.BatchSize &&
                a.UBatchSize == b.UBatchSize &&
                a.MinP == b.MinP &&
                a.MmprojPath == b.MmprojPath &&
                a.CacheTypeK == b.CacheTypeK &&
                a.CacheTypeV == b.CacheTypeV &&
                a.TopK == b.TopK &&
                a.TopP == b.TopP &&
                a.RepeatPenalty == b.RepeatPenalty &&
                a.FlashAttention == b.FlashAttention &&
                a.EnableWebUI == b.EnableWebUI &&
                a.EmbeddingMode == b.EmbeddingMode &&
                a.EnableSlots == b.EnableSlots &&
                a.EnableMetrics == b.EnableMetrics &&
            a.ApiKey == b.ApiKey &&
                a.LogFilePath == b.LogFilePath &&
                a.VerboseLogging == b.VerboseLogging &&
                a.Alias == b.Alias &&
                a.CustomArguments == b.CustomArguments;
    }

    public string ProfileNameInput
    {
        get => _profileNameInput;
        set { _profileNameInput = value; OnPropertyChanged(); }
    }

    public bool IsServerRunning
    {
        get => _isServerRunning;
        set
        {
            if (_isServerRunning != value)
            {
                _isServerRunning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(WindowTitleWithProfile));
                // Notify commands that depend on IsServerRunning
                if (StopServerCommand is AsyncRelayCommand stopCmd)
                    stopCmd.RaiseCanExecuteChanged();
                if (UnloadModelCommand is AsyncRelayCommand unloadCmd)
                    unloadCmd.RaiseCanExecuteChanged();
            }
        }
    }

    public string ServerStatus
    {
        get => _serverStatus;
        set { _serverStatus = value; OnPropertyChanged(); }
    }

    public string CurrentLog
    {
        get => _currentLog;
        set { _currentLog = value; OnPropertyChanged(); }
    }

    public ObservableCollection<string> Profiles { get; }
    public ObservableCollection<string> LogLines { get; } = new();

    public string LogOutput
    {
        get => _logOutput;
        set { _logOutput = value; OnPropertyChanged(); }
    }

    public string LogText
    {
        get => _logText;
        private set { _logText = value; OnPropertyChanged(); }
    }

    public string CurrentCommand
    {
        get => _currentCommand;
        private set { _currentCommand = value; OnPropertyChanged(); }
    }

    public ICommand BrowseExecutableCommand { get; }
    public ICommand BrowseModelCommand { get; }
    public ICommand BrowseModelsDirCommand { get; }
    public ICommand BrowseLogFileCommand { get; }
    public ICommand BrowseMmprojCommand { get; }
    public ICommand StartServerCommand { get; }
    public ICommand StopServerCommand { get; }
    public ICommand UnloadModelCommand { get; }
    public ICommand SaveProfileCommand { get; }
    public ICommand LoadProfileCommand { get; }
    public ICommand DeleteProfileCommand { get; }
    public ICommand RenameProfileCommand { get; }
    public ICommand ExportProfileCommand { get; }
    public ICommand ExportToBatCommand { get; }
    public ICommand ImportProfileCommand { get; }
    public ICommand ExportAllCommand { get; }
    public ICommand ImportAllCommand { get; }
    public ICommand ClearAllFieldsCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand ShowWindowCommand { get; set; }
    public ICommand CloseFromTrayCommand { get; set; }

    public bool CanStartServer => !string.IsNullOrEmpty(ExecutablePath) && 
                                    (!string.IsNullOrEmpty(ModelPath) || !string.IsNullOrEmpty(ModelsDir));

    private async Task BrowseExecutableAsync()
    {
        var result = await WindowsFileDialogs.OpenFileDialogAsync(
            "Select llama-server executable",
            new[] { ("All files", "*") },
            false);
        if (result != null && result.Length > 0)
        {
            ExecutablePath = result[0];
        }
    }

    private void UpdateCurrentCommand()
    {
        var config = GetCurrentConfig();
        CurrentCommand = CommandLineBuilder.BuildFullCommand(config);
    }

    private async Task BrowseModelAsync()
    {
        var result = await WindowsFileDialogs.OpenFileDialogAsync(
            "Select Model File",
            new[] { ("Model files", "*.gguf"), ("All files", "*.*") },
            false);
        if (result != null && result.Length > 0)
        {
            ModelPath = result[0];
        }
    }

    private async Task BrowseModelsDirAsync()
    {
        var result = await WindowsFileDialogs.OpenFolderDialogAsync("Select Models Directory");
        if (!string.IsNullOrEmpty(result))
        {
            ModelsDir = result;
        }
    }

    private async Task BrowseLogFileAsync()
    {
        var result = await WindowsFileDialogs.SaveFileDialogAsync(
            "Select Log File",
            "log",
            new[] { ("Log files", "*.log"), ("All files", "*.*") });
        if (!string.IsNullOrEmpty(result))
        {
            LogFilePath = result;
        }
    }

    private async Task BrowseMmprojAsync()
    {
        var result = await WindowsFileDialogs.OpenFileDialogAsync(
            "Select MMProj File",
            new[] { ("GGUF files", "*.gguf"), ("All files", "*.*") },
            false);
        if (result != null && result.Length > 0)
        {
            MmprojPath = result[0];
        }
    }

    public ServerConfiguration GetCurrentConfig()
    {
        return new ServerConfiguration
        {
            ExecutablePath = ExecutablePath,
            ModelPath = ModelPath,
            ModelsDir = ModelsDir,
            Host = Host,
            Port = Port,
            ContextSize = ParseNullableInt(ContextSize),
            Threads = ParseNullableInt(Threads),
            GpuLayers = ParseNullableInt(GpuLayers),
            Temperature = ParseNullableDouble(Temperature),
            MaxTokens = ParseNullableInt(MaxTokens),
            BatchSize = ParseNullableInt(BatchSize),
            UBatchSize = ParseNullableInt(UBatchSize),
            MinP = ParseNullableDouble(MinP),
            MmprojPath = MmprojPath,
            CacheTypeK = CacheTypeK,
            CacheTypeV = CacheTypeV,
            TopK = ParseNullableInt(TopK),
            TopP = ParseNullableDouble(TopP),
            RepeatPenalty = ParseNullableDouble(RepeatPenalty),
            FlashAttention = FlashAttention,
            EnableWebUI = EnableWebUI,
            EmbeddingMode = EmbeddingMode,
            EnableSlots = EnableSlots,
            EnableMetrics = EnableMetrics,
            ApiKey = ApiKey,
            LogFilePath = LogFilePath,
            VerboseLogging = VerboseLogging,
            Alias = Alias,
            CustomArguments = CustomArguments
        };
    }

private void LoadConfigToUI(ServerConfiguration config)
    {
        // Clear all fields except profile-related fields
        ExecutablePath = string.Empty;
        ModelPath = string.Empty;
        ModelsDir = string.Empty;
        Host = "127.0.0.1";
        Port = 8080;
        ContextSize = string.Empty;
        Threads = string.Empty;
        GpuLayers = string.Empty;
        Temperature = string.Empty;
        MaxTokens = string.Empty;
        BatchSize = string.Empty;
        UBatchSize = string.Empty;
        MinP = string.Empty;
        TopK = string.Empty;
        TopP = string.Empty;
        RepeatPenalty = string.Empty;
        FlashAttention = null;
        EnableWebUI = null;
        EmbeddingMode = null;
        EnableSlots = null;
        EnableMetrics = null;
        ApiKey = string.Empty;
        LogFilePath = string.Empty;
        MmprojPath = string.Empty;
        CacheTypeK = string.Empty;
        CacheTypeV = string.Empty;
        VerboseLogging = false;
        Alias = string.Empty;
        CustomArguments = string.Empty;
        AutoRestart = false;
        // Note: ProfileNameInput and _loadedProfileName are NOT cleared here
        // They are preserved when loading a profile
        
        // Then load the new config values
        ExecutablePath = config.ExecutablePath ?? string.Empty;
        ModelPath = config.ModelPath ?? string.Empty;
        ModelsDir = config.ModelsDir ?? string.Empty;
        Host = config.Host ?? "127.0.0.1";
        Port = config.Port;
        ContextSize = config.ContextSize?.ToString() ?? string.Empty;
        Threads = config.Threads?.ToString() ?? string.Empty;
        GpuLayers = config.GpuLayers?.ToString() ?? string.Empty;
        Temperature = config.Temperature?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        MaxTokens = config.MaxTokens?.ToString() ?? string.Empty;
        BatchSize = config.BatchSize?.ToString() ?? string.Empty;
        UBatchSize = config.UBatchSize?.ToString() ?? string.Empty;
        MinP = config.MinP?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        TopK = config.TopK?.ToString() ?? string.Empty;
        TopP = config.TopP?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        RepeatPenalty = config.RepeatPenalty?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        FlashAttention = config.FlashAttention;
        EnableWebUI = config.EnableWebUI;
        EmbeddingMode = config.EmbeddingMode;
        EnableSlots = config.EnableSlots;
        EnableMetrics = config.EnableMetrics;
        ApiKey = config.ApiKey ?? string.Empty;
        LogFilePath = config.LogFilePath ?? string.Empty;
        MmprojPath = config.MmprojPath ?? string.Empty;
        CacheTypeK = config.CacheTypeK ?? string.Empty;
        CacheTypeV = config.CacheTypeV ?? string.Empty;
        VerboseLogging = config.VerboseLogging;
        Alias = config.Alias ?? string.Empty;
        _disabledArguments.Clear();
        _originalCustomArguments = string.Empty;
        CustomArguments = config.CustomArguments ?? string.Empty;
        ParseCustomArguments();
        RebuildCustomArgumentsFromToggles();
    }

    public void ClearAllFields()
    {
        ExecutablePath = string.Empty;
        ModelPath = string.Empty;
        ModelsDir = string.Empty;
        Host = "127.0.0.1";
        Port = 8080;
        ContextSize = string.Empty;
        Threads = string.Empty;
        GpuLayers = string.Empty;
        Temperature = string.Empty;
        MaxTokens = string.Empty;
        BatchSize = string.Empty;
        UBatchSize = string.Empty;
        MinP = string.Empty;
        TopK = string.Empty;
        TopP = string.Empty;
        RepeatPenalty = string.Empty;
        FlashAttention = null;
        EnableWebUI = null;
        EmbeddingMode = null;
        EnableSlots = null;
        EnableMetrics = null;
        ApiKey = string.Empty;
        LogFilePath = string.Empty;
        MmprojPath = string.Empty;
        CacheTypeK = string.Empty;
        CacheTypeV = string.Empty;
        VerboseLogging = false;
        Alias = string.Empty;
        _disabledArguments.Clear();
        _originalCustomArguments = string.Empty;
        CustomArgumentItems.Clear();
        CustomArguments = string.Empty;
        AutoRestart = false;
        // ProfileNameInput = string.Empty;
        _loadedProfileName = string.Empty;
        _loadedProfileConfig = null;
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private static int? ParseNullableInt(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return int.TryParse(value, out var result) ? result : null;
    }

    private static double? ParseNullableDouble(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    private void ParseCustomArguments()
    {
        CustomArgumentItems.Clear();
        if (string.IsNullOrWhiteSpace(_customArguments))
        {
            _originalCustomArguments = string.Empty;
            return;
        }

        var normalized = System.Text.RegularExpressions.Regex.Replace(_customArguments.Trim(), @"[ \t\r\n]+", " ");
        var tokens = TokenizeArgs(normalized);

        int i = 0;
        while (i < tokens.Count)
        {
            var token = tokens[i];
            bool isFlag = token.StartsWith("-");

            if (isFlag && i + 1 < tokens.Count)
            {
                var next = tokens[i + 1];
                bool nextIsFlag = next.StartsWith("-");
                bool nextIsQuotedValue = (next.StartsWith("\"") && next.EndsWith("\"")) ||
                                         (next.StartsWith("'") && next.EndsWith("'")) ||
                                         (next.StartsWith("{") && next.EndsWith("}")) ||
                                         (next.StartsWith("[") && next.EndsWith("]"));

                if (nextIsQuotedValue)
                {
                    CustomArgumentItems.Add(new CustomArgumentItem
                    {
                        Name = token,
                        Value = null,
                        OriginalArg = $"{token} {next}"
                    });
                    i += 2;
                }
                else if (!nextIsFlag)
                {
                    CustomArgumentItems.Add(new CustomArgumentItem
                    {
                        Name = token,
                        Value = next,
                        OriginalArg = $"{token} {next}"
                    });
                    i += 2;
                }
                else
                {
                    CustomArgumentItems.Add(new CustomArgumentItem
                    {
                        Name = token,
                        Value = null,
                        OriginalArg = token
                    });
                    i++;
                }
            }
            else
            {
                CustomArgumentItems.Add(new CustomArgumentItem
                {
                    Name = token,
                    Value = null,
                    OriginalArg = token
                });
                i++;
            }
        }

        _originalCustomArguments = _customArguments;
    }

    /// <summary>
    /// Real-time toggle update during typing: parses the current text into
    /// enabled items and preserves disabled items from _originalCustomArguments
    /// without modifying _originalCustomArguments.
    /// </summary>
    private void UpdateCustomArgumentTogglesFromText()
    {
        var currTokens = string.IsNullOrWhiteSpace(_customArguments)
            ? new List<string>()
            : TokenizeArgs(_customArguments.Trim());
        var currPairs = ParseTokensToArgPairs(currTokens);

        // Track which names appear in current text
        var currNames = new HashSet<string>();
        foreach (var pair in currPairs)
            currNames.Add(pair.Name);

        CustomArgumentItems.Clear();

        // Add items from current text (all enabled)
        foreach (var pair in currPairs)
        {
            CustomArgumentItems.Add(new CustomArgumentItem
            {
                Name = pair.Name,
                Value = pair.Value,
                OriginalArg = pair.OriginalArg,
                IsEnabled = true
            });
        }

        // Preserve disabled items from _originalCustomArguments that aren't in current text
        if (!string.IsNullOrWhiteSpace(_originalCustomArguments))
        {
            var origTokens = TokenizeArgs(_originalCustomArguments.Trim());
            var origPairs = ParseTokensToArgPairs(origTokens);
            foreach (var origPair in origPairs)
            {
                if (!currNames.Contains(origPair.Name))
                {
                    CustomArgumentItems.Add(new CustomArgumentItem
                    {
                        Name = origPair.Name,
                        Value = origPair.Value,
                        OriginalArg = origPair.OriginalArg,
                        IsEnabled = false
                    });
                }
            }
        }
    }

    private List<string> TokenizeArgs(string input)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(input))
            return result;

        int i = 0;
        while (i < input.Length)
        {
            char c = input[i];

            if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
            {
                i++;
                continue;
            }

            if (c == '"' || c == '\'')
            {
                char quote = c;
                int end = -1;
                for (int j = i + 1; j < input.Length; j++)
                {
                    if (input[j] == '\\' && j + 1 < input.Length)
                    {
                        j++;
                        continue;
                    }
                    if (input[j] == quote)
                    {
                        end = j;
                        break;
                    }
                }
                if (end == -1) end = input.Length - 1;
                result.Add(input.Substring(i, end - i + 1));
                i = end + 1;
                continue;
            }

            if (c == '{')
            {
                int end = -1;
                int depth = 0;
                for (int j = i; j < input.Length; j++)
                {
                    char ch = input[j];
                    if (ch == '{') depth++;
                    else if (ch == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            end = j;
                            break;
                        }
                    }
                    else if (ch == '"' || ch == '\'')
                    {
                        char quote = ch;
                        for (int k = j + 1; k < input.Length; k++)
                        {
                            if (input[k] == '\\' && k + 1 < input.Length)
                            {
                                k++;
                                continue;
                            }
                            if (input[k] == quote)
                            {
                                j = k;
                                break;
                            }
                        }
                    }
                }
                if (end == -1) end = input.Length - 1;
                result.Add(input.Substring(i, end - i + 1));
                i = end + 1;
                continue;
            }

            if (c == '[')
            {
                int end = -1;
                int depth = 0;
                for (int j = i; j < input.Length; j++)
                {
                    char ch = input[j];
                    if (ch == '[') depth++;
                    else if (ch == ']')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            end = j;
                            break;
                        }
                    }
                    else if (ch == '"' || ch == '\'')
                    {
                        char quote = ch;
                        for (int k = j + 1; k < input.Length; k++)
                        {
                            if (input[k] == '\\' && k + 1 < input.Length)
                            {
                                k++;
                                continue;
                            }
                            if (input[k] == quote)
                            {
                                j = k;
                                break;
                            }
                        }
                    }
                }
                if (end == -1) end = input.Length - 1;
                result.Add(input.Substring(i, end - i + 1));
                i = end + 1;
                continue;
            }

            int spaceIdx = input.IndexOf(' ', i);
            int tabIdx = input.IndexOf('\t', i);
            int nextSpecial = Math.Min(spaceIdx >= 0 ? spaceIdx : input.Length,
                                       tabIdx >= 0 ? tabIdx : input.Length);
            if (nextSpecial == input.Length) nextSpecial = -1;

            int flagSpace = -1;
            for (int j = i + 1; j < input.Length; j++)
            {
                if (input[j] == ' ' || input[j] == '\t' || input[j] == '\r' || input[j] == '\n')
                {
                    flagSpace = j;
                    break;
                }
            }

            if (flagSpace > 0 && flagSpace < input.Length)
            {
                result.Add(input.Substring(i, flagSpace - i));
                i = flagSpace;
            }
            else
            {
                result.Add(input.Substring(i));
                break;
            }
        }

        return result;
    }

public void RebuildCustomArgumentsFromToggles()
    {
        _isUpdatingCustomArguments = true;
        try
        {
            var disabledItems = CustomArgumentItems.Where(x => !x.IsEnabled).Select(x => x.OriginalArg).ToList();

            var visibleText = string.Join(" ", CustomArgumentItems
                .Where(x => x.IsEnabled)
                .Select(x => x.OriginalArg));

            _customArguments = visibleText;
            _disabledArguments.Clear();
            foreach (var item in disabledItems)
                _disabledArguments[item] = false;
            OnPropertyChanged(nameof(CustomArguments));
            OnPropertyChanged(nameof(HasUnsavedChanges));
            UpdateCurrentCommand();
        }
        finally
        {
            _isUpdatingCustomArguments = false;
        }
    }

    public void ClearCustomArguments()
    {
        _disabledArguments.Clear();
        _originalCustomArguments = string.Empty;
        CustomArgumentItems.Clear();
        _customArguments = string.Empty;
        OnPropertyChanged(nameof(CustomArguments));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        UpdateCurrentCommand();
    }

    private void ResetCustomArguments()
    {
        _customArguments = _originalCustomArguments;
        OnPropertyChanged(nameof(CustomArguments));
        ParseCustomArguments();
        OnPropertyChanged(nameof(HasUnsavedChanges));
        UpdateCurrentCommand();
    }

    public void RemoveCustomArgument(CustomArgumentItem item)
    {
        // Use arg-pair-level filtering so multi-token args (e.g. --flag "value")
        // are removed as a unit, not compared token-by-token.
        var tokens = TokenizeArgs(_originalCustomArguments);
        var pairs = ParseTokensToArgPairs(tokens);
        _originalCustomArguments = string.Join(" ", pairs
            .Where(p => p.OriginalArg != item.OriginalArg)
            .Select(p => p.OriginalArg));
        _disabledArguments.Remove(item.OriginalArg);
        CustomArgumentItems.Remove(item);
        RebuildCustomArgumentsFromToggles();
    }

    public void ParseAndUpdateCustomArguments()
    {
        var currTokens = string.IsNullOrWhiteSpace(_customArguments)
            ? new List<string>()
            : TokenizeArgs(_customArguments.Trim());
        var origTokens = string.IsNullOrWhiteSpace(_originalCustomArguments)
            ? new List<string>()
            : TokenizeArgs(_originalCustomArguments.Trim());

        var currPairs = ParseTokensToArgPairs(currTokens);
        var origPairs = ParseTokensToArgPairs(origTokens);

        // Merge: match original args with current args by name (first unmatched wins)
        var merged = new List<(string Name, string? Value, string OriginalArg)>();
        var matchedCurrIndices = new HashSet<int>();

        foreach (var origPair in origPairs)
        {
            int matchIdx = -1;
            for (int j = 0; j < currPairs.Count; j++)
            {
                if (!matchedCurrIndices.Contains(j) && currPairs[j].Name == origPair.Name)
                {
                    matchIdx = j;
                    break;
                }
            }

            if (matchIdx >= 0)
            {
                // Arg exists in current text — use current version (value may have changed)
                merged.Add(currPairs[matchIdx]);
                matchedCurrIndices.Add(matchIdx);
            }
            else
            {
                // Arg was toggled off or removed from text — keep original version
                merged.Add(origPair);
            }
        }

        // Append genuinely new arguments from current text
        for (int j = 0; j < currPairs.Count; j++)
        {
            if (!matchedCurrIndices.Contains(j))
            {
                merged.Add(currPairs[j]);
            }
        }

        _originalCustomArguments = string.Join(" ", merged.Select(p => p.OriginalArg));

        // Rebuild CustomArgumentItems and toggle state
        CustomArgumentItems.Clear();
        _disabledArguments.Clear();

        foreach (var pair in merged)
        {
            bool isInCurrentText = currPairs.Any(cp => cp.Name == pair.Name);
            var item = new CustomArgumentItem
            {
                Name = pair.Name,
                Value = pair.Value,
                OriginalArg = pair.OriginalArg,
                IsEnabled = isInCurrentText
            };

            if (!item.IsEnabled)
            {
                _disabledArguments[pair.OriginalArg] = false;
            }

            CustomArgumentItems.Add(item);
        }

        RebuildCustomArgumentsFromToggles();
    }

    /// <summary>
    /// Parses flat tokens into structured (Name, Value, OriginalArg) pairs,
    /// grouping flags with their values when applicable.
    /// </summary>
    private List<(string Name, string? Value, string OriginalArg)> ParseTokensToArgPairs(List<string> tokens)
    {
        var pairs = new List<(string Name, string? Value, string OriginalArg)>();
        int i = 0;
        while (i < tokens.Count)
        {
            var token = tokens[i];
            bool isFlag = token.StartsWith("-");

            if (isFlag && i + 1 < tokens.Count)
            {
                var next = tokens[i + 1];
                bool nextIsFlag = next.StartsWith("-");
                bool nextIsQuotedValue = (next.StartsWith("\"") && next.EndsWith("\"")) ||
                                         (next.StartsWith("'") && next.EndsWith("'")) ||
                                         (next.StartsWith("{") && next.EndsWith("}")) ||
                                         (next.StartsWith("[") && next.EndsWith("]"));

                if (nextIsQuotedValue)
                {
                    pairs.Add((token, null, $"{token} {next}"));
                    i += 2;
                }
                else if (!nextIsFlag)
                {
                    pairs.Add((token, next, $"{token} {next}"));
                    i += 2;
                }
                else
                {
                    pairs.Add((token, null, token));
                    i++;
                }
            }
            else
            {
                pairs.Add((token, null, token));
                i++;
            }
        }
        return pairs;
    }

    public void ApplyToggleStates(Dictionary<string, bool> states)
    {
        foreach (var item in CustomArgumentItems)
        {
            if (states.TryGetValue(item.Name, out var enabled))
                item.IsEnabled = enabled;
        }
        RebuildCustomArgumentsFromToggles();
    }

    public Dictionary<string, bool> GetToggleStates()
    {
        var states = new Dictionary<string, bool>();
        foreach (var item in CustomArgumentItems)
            states[item.Name] = item.IsEnabled;
        return states;
    }

    private async Task StartServerAsync()
    {
        try
        {
            var config = GetCurrentConfig();
            await _serverService.StartAsync(config);
        }
        catch (Exception ex)
        {
            var message = string.Format(LocalizedStrings.Instance.FailedToStartServer, ex.Message);
            await ShowErrorAsync(message);
        }
    }

    private async Task ShowErrorAsync(string message)
    {
        var dialog = new Window
        {
            Title = LocalizedStrings.Instance.ErrorTitle,
            Width = 400,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        var panel = new StackPanel { Margin = new Avalonia.Thickness(10) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Avalonia.Thickness(5) });
        var okButton = new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new Avalonia.Thickness(5) };
        okButton.Click += (s, e) => dialog.Close();
        panel.Children.Add(okButton);
        dialog.Content = panel;
        await dialog.ShowDialog(MainWindow.Instance!);
    }

    private async Task StopServerAsync()
    {
        await _serverService.StopAsync();
    }

    public async Task StopServerIfRunningAsync()
    {
        if (_serverService.IsRunning)
        {
            await _serverService.StopAsync();
        }
    }

    private async Task UnloadModelAsync()
    {
        await _serverService.UnloadModelAsync();
    }

    private void LoadProfiles()
    {
        Profiles.Clear();
        var profiles = _configService.GetAllProfiles();
        foreach (var profile in profiles)
        {
            Profiles.Add(profile.Name);
        }
    }

    private async Task SaveProfileAsync()
    {
        var name = !string.IsNullOrWhiteSpace(ProfileNameInput) ? ProfileNameInput : SelectedProfile;
        if (string.IsNullOrWhiteSpace(name))
        {
            await ShowWarningAsync(LocalizedStrings.Instance.PleaseEnterProfileName);
            return;
        }

        var config = GetCurrentConfig();
        await _configService.SaveProfileAsync(name, config);
        
        ProfileNameInput = string.Empty;
        LoadProfiles();
        
        SelectedProfile = name;
        _loadedProfileName = name;
        _loadedProfileConfig = config;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(WindowTitleWithProfile));
    }

    private async Task ShowWarningAsync(string message)
    {
        var dialog = new Window
        {
            Title = LocalizedStrings.Instance.WarningTitle,
            Width = 400,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        var panel = new StackPanel { Margin = new Avalonia.Thickness(10) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Avalonia.Thickness(5) });
        var okButton = new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new Avalonia.Thickness(5) };
        okButton.Click += (s, e) => dialog.Close();
        panel.Children.Add(okButton);
        dialog.Content = panel;
        await dialog.ShowDialog(MainWindow.Instance!);
    }

    private async Task LoadProfileAsync()
    {
        var name = SelectedProfile;
        if (string.IsNullOrWhiteSpace(name)) return;

        if (HasUnsavedChanges)
        {
            var result = await ShowConfirmAsync(
                LocalizedStrings.Instance.UnsavedChangesMessage,
                LocalizedStrings.Instance.UnsavedChangesTitle);

            if (result == MessageBoxResult.Cancel)
                return;
            
            if (result == MessageBoxResult.Yes)
            {
                // Save to CURRENT profile (_loadedProfileName), not to the one we're trying to load
                var saveName = _loadedProfileName;
                if (string.IsNullOrWhiteSpace(saveName))
                {
                    // Fall back to ProfileNameInput if no loaded profile
                    saveName = ProfileNameInput;
                }
                
                if (string.IsNullOrWhiteSpace(saveName))
                {
                    await ShowWarningAsync(LocalizedStrings.Instance.PleaseEnterProfileName);
                    return;
                }
                
                var config = GetCurrentConfig();
                await _configService.SaveProfileAsync(saveName, config);
                ProfileNameInput = string.Empty;
                LoadProfiles();
                // Update current profile state but don't return - continue to load the new profile
                SelectedProfile = saveName;
                _loadedProfileName = saveName;
                _loadedProfileConfig = config;
                OnPropertyChanged(nameof(HasUnsavedChanges));
                // Continue to load the new profile below
            }
        }

        var loadedConfig = await _configService.LoadProfileAsync(name);
        if (loadedConfig != null)
        {
            LoadConfigToUI(loadedConfig);
            _loadedProfileConfig = loadedConfig;
            _loadedProfileName = name;
            OnPropertyChanged(nameof(HasUnsavedChanges));
            OnPropertyChanged(nameof(WindowTitleWithProfile));
        }
    }

    private async Task<MessageBoxResult> ShowConfirmAsync(string message, string title)
    {
        var result = await MessageBox.ShowAsync(
            MainWindow.Instance!,
            message,
            title,
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);
        return result;
    }

    private async Task DeleteProfileAsync()
    {
        var name = SelectedProfile;
        if (string.IsNullOrWhiteSpace(name)) return;

        var result = await ShowConfirmAsync(
            string.Format(LocalizedStrings.GetString("ConfirmDelete"), name),
            LocalizedStrings.Instance.ConfirmTitle);

        if (result == MessageBoxResult.Yes)
        {
            await _configService.DeleteProfileAsync(name);
            LoadProfiles();
        }
    }

    private async Task RenameProfileAsync()
    {
        var oldName = SelectedProfile;
        if (string.IsNullOrWhiteSpace(oldName)) return;

        // Ask for new name using a simple input dialog
        var newName = await ShowRenameDialogAsync(oldName);
        if (string.IsNullOrWhiteSpace(newName) || newName == oldName) return;

        try
        {
            await _configService.RenameProfileAsync(oldName, newName);
            _loadedProfileName = newName;
            LoadProfiles();
            SelectedProfile = newName;
            OnPropertyChanged(nameof(WindowTitleWithProfile));
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(string.Format(LocalizedStrings.GetString("RenameProfileFailed"), ex.Message));
        }
    }

    private async Task<string> ShowRenameDialogAsync(string currentName)
    {
        var dialog = new Window
        {
            Title = LocalizedStrings.GetString("Rename"),
            Width = 400,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var panel = new StackPanel { Margin = new Avalonia.Thickness(15) };
        
        panel.Children.Add(new TextBlock 
        { 
            Text = LocalizedStrings.GetString("EnterNewProfileName"),
            Margin = new Avalonia.Thickness(5) 
        });

        var textBox = new TextBox 
        { 
            Text = currentName,
            Margin = new Avalonia.Thickness(5, 10) 
        };
        panel.Children.Add(textBox);

        var buttonPanel = new StackPanel 
        { 
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Avalonia.Thickness(5, 10, 5, 5)
        };

        var okButton = new Button { Content = "OK", Margin = new Avalonia.Thickness(5) };
        var cancelButton = new Button { Content = LocalizedStrings.GetString("Cancel"), Margin = new Avalonia.Thickness(5) };
        
        string? result = null;
        okButton.Click += (s, e) => { result = textBox.Text; dialog.Close(); };
        cancelButton.Click += (s, e) => { dialog.Close(); };
        
        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        panel.Children.Add(buttonPanel);
        
        dialog.Content = panel;
        
        await dialog.ShowDialog(MainWindow.Instance!);
        return result ?? string.Empty;
    }

    private async Task ExportProfileAsync()
    {
        var filePath = await WindowsFileDialogs.SaveFileDialogAsync(
            LocalizedStrings.Instance.ExportDialogTitle,
            "json",
            new[] { 
                ("JSON profile (*.json)", "*.json"), 
                ("Windows batch (*.bat)", "*.bat"),
                ("MacOS script (*.command)", "*.command"),
                ("Linux script (*.sh)", "*.sh")
            });
        
        if (!string.IsNullOrEmpty(filePath))
        {
            var config = GetCurrentConfig();
            
            if (filePath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            {
                var command = CommandLineBuilder.BuildFullCommand(config);
                var batContent = $"@echo off\n{command}\npause";
                await System.IO.File.WriteAllTextAsync(filePath, batContent);
                _logService.Info($"Profile exported to BAT '{filePath}'");
            }
            else if (filePath.EndsWith(".command", StringComparison.OrdinalIgnoreCase))
            {
                var command = CommandLineBuilder.BuildFullCommand(config);
                var commandContent = $"#!/bin/bash\n{command}\necho 'Press Enter to exit...'\nread";
                await System.IO.File.WriteAllTextAsync(filePath, commandContent);
                _logService.Info($"Profile exported to MacOS command script '{filePath}'");
            }
            else if (filePath.EndsWith(".sh", StringComparison.OrdinalIgnoreCase))
            {
                var command = CommandLineBuilder.BuildFullCommand(config);
                var shContent = $"#!/bin/bash\n{command}\necho 'Press Enter to exit...'\nread";
                await System.IO.File.WriteAllTextAsync(filePath, shContent);
                _logService.Info($"Profile exported to Linux shell script '{filePath}'");
            }
            else
            {
                await _configService.ExportProfileAsync(filePath, config);
            }
        }
    }

    private async Task ImportProfileAsync()
    {
        var result = await WindowsFileDialogs.OpenFileDialogAsync(
            "Import Profile",
            new[] { ("JSON files", "*.json") },
            false);
        if (result != null && result.Length > 0)
        {
            var config = await _configService.ImportProfileAsync(result[0]);
            if (config != null)
            {
                LoadConfigToUI(config);
            }
        }
    }

    private async Task ExportAllProfilesAsync()
    {
        var profiles = _configService.GetAllProfiles();
        if (profiles.Count == 0)
        {
            await ShowWarningAsync(LocalizedStrings.Instance.NoProfilesToExport);
            return;
        }

        var filePath = await WindowsFileDialogs.SaveFileDialogAsync(
            LocalizedStrings.Instance.ExportAllDialogTitle,
            "zip",
            new[] { (LocalizedStrings.Instance.ExportAllFilter, "*.zip") });

        if (!string.IsNullOrEmpty(filePath))
        {
            try
            {
                await _configService.ExportAllProfilesAsync(filePath);
                await ShowInfoAsync(string.Format(LocalizedStrings.Instance.ExportAllSuccess, profiles.Count));
            }
            catch (Exception ex)
            {
                await ShowErrorAsync(string.Format(LocalizedStrings.Instance.ExportAllFailed, ex.Message));
            }
        }
    }

    private async Task ImportAllProfilesAsync()
    {
        var result = await WindowsFileDialogs.OpenFileDialogAsync(
            LocalizedStrings.Instance.ImportAllDialogTitle,
            new[] { ("Backup archive", "*.zip") });

        if (result != null && result.Length > 0)
        {
            try
            {
                var importResult = await _configService.ImportAllProfilesAsync(result[0]);
                if (importResult.Success)
                {
                    LoadProfiles();
                    await ShowInfoAsync(string.Format(
                        LocalizedStrings.Instance.ImportAllSuccess,
                        importResult.ImportedCount,
                        importResult.SkippedCount));
                }
                else
                {
                    await ShowErrorAsync(string.Format(LocalizedStrings.Instance.ImportAllFailed, importResult.ErrorMessage));
                }
            }
            catch (Exception ex)
            {
                await ShowErrorAsync(string.Format(LocalizedStrings.Instance.ImportAllFailed, ex.Message));
            }
        }
    }

    private async Task ShowInfoAsync(string message)
    {
        var dialog = new Window
        {
            Title = LocalizedStrings.Instance.ConfirmTitle,
            Width = 400,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        var panel = new StackPanel { Margin = new Avalonia.Thickness(10) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Avalonia.Thickness(5) });
        var okButton = new Button { Content = LocalizedStrings.Instance.OK, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new Avalonia.Thickness(5) };
        okButton.Click += (s, e) => dialog.Close();
        panel.Children.Add(okButton);
        dialog.Content = panel;
        await dialog.ShowDialog(MainWindow.Instance!);
    }

    public void LoadConfigFromCommandLine(ServerConfiguration config)
    {
        ExecutablePath = string.Empty;
        ModelPath = string.Empty;
        ModelsDir = string.Empty;
        Host = "127.0.0.1";
        Port = 8080;
        ContextSize = string.Empty;
        Threads = string.Empty;
        GpuLayers = string.Empty;
        Temperature = string.Empty;
        MaxTokens = string.Empty;
        BatchSize = string.Empty;
        UBatchSize = string.Empty;
        MinP = string.Empty;
        FlashAttention = null;
        EnableWebUI = null;
        EmbeddingMode = null;
        EnableSlots = null;
        EnableMetrics = null;
        ApiKey = string.Empty;
        LogFilePath = string.Empty;
        VerboseLogging = false;
        Alias = string.Empty;

        LoadConfigToUI(config);
    }

    private async Task ExportToBatAsync()
    {
        var filePath = await WindowsFileDialogs.SaveFileDialogAsync(
            "Export to BAT",
            "bat",
            new[] { ("BAT files", "*.bat") });
        if (!string.IsNullOrEmpty(filePath))
        {
            var config = GetCurrentConfig();
            var command = CommandLineBuilder.BuildFullCommand(config);
            var batContent = $"@echo off\n{command}\npause";
            await System.IO.File.WriteAllTextAsync(filePath, batContent);
        }
    }

    private void ClearLog()
    {
        LogLines.Clear();
        LogText = string.Empty;
    }

    private void OnLogReceived(object? sender, string logLine)
    {
        if (!LogEnabled) return;
        Dispatcher.UIThread.Invoke(() =>
        {
            LogLines.Add(logLine);
            if (LogLines.Count > 1000)
            {
                LogLines.RemoveAt(0);
            }
            LogText = string.Join("\n", LogLines);
        });
    }

    private void OnServerOutput(object? sender, string output)
    {
        if (!LogEnabled) return;
        Dispatcher.UIThread.Invoke(() =>
        {
            LogLines.Add(output);
            if (LogLines.Count > 1000)
            {
                LogLines.RemoveAt(0);
            }
            LogText = string.Join("\n", LogLines);
        });
    }

    private bool _isAutoRestarting;

    private async void OnServerStateChanged(object? sender, bool isRunning)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                IsServerRunning = isRunning;
                ServerStatus = isRunning 
                    ? string.Format(Resources.LocalizedStrings.GetString("StatusRunning"), _serverService.ProcessId) 
                    : Localized.StatusStopped;

                if (!isRunning && AutoRestart && !_isAutoRestarting && !_serverService.WasStoppedIntentionally)
                {
                    _logService.AppLog("Server exited unexpectedly. Auto-restarting...");
                    _isAutoRestarting = true;
                    await Task.Delay(1000);
                    try
                    {
                        var config = GetCurrentConfig();
                        await _serverService.StartAsync(config);
                    }
                    catch (Exception ex)
                    {
                        await ShowErrorAsync(string.Format(LocalizedStrings.Instance.FailedToAutoRestart, ex.Message));
                    }
                    finally
                    {
                        _isAutoRestarting = false;
                    }
                }
            });
        }
        catch (TaskCanceledException)
        {
            // Ignore - dispatcher is shutting down
        }
    }

    public async Task SaveSettingsAsync()
    {
        var settings = GetAppSettings();
        await _configService.SaveAppSettingsAsync(settings);
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class LanguageOption
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}

public class CustomArgumentItem : INotifyPropertyChanged
{
    private bool _isEnabled = true;
    public string Name { get; set; } = "";
    public string? Value { get; set; }
    public string OriginalArg { get; set; } = "";

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public enum MessageBoxResult
{
    None,
    OK,
    Cancel,
    Yes,
    No
}

public static class MessageBox
{
    public static async Task<MessageBoxResult> ShowAsync(Window owner, string message, string title, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var panel = new StackPanel { Margin = new Avalonia.Thickness(15) };
        
        panel.Children.Add(new TextBlock 
        { 
            Text = message, 
            TextWrapping = Avalonia.Media.TextWrapping.Wrap, 
            Margin = new Avalonia.Thickness(5) 
        });

        var buttonPanel = new StackPanel 
        { 
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Avalonia.Thickness(5, 10, 5, 5)
        };

        MessageBoxResult result = MessageBoxResult.None;

        if (buttons == MessageBoxButtons.YesNoCancel)
        {
            var yesButton = new Button { Content = "Yes", Margin = new Avalonia.Thickness(5) };
            yesButton.Click += (s, e) => { result = MessageBoxResult.Yes; dialog.Close(); };
            buttonPanel.Children.Add(yesButton);

            var noButton = new Button { Content = "No", Margin = new Avalonia.Thickness(5) };
            noButton.Click += (s, e) => { result = MessageBoxResult.No; dialog.Close(); };
            buttonPanel.Children.Add(noButton);

            var cancelButton = new Button { Content = "Cancel", Margin = new Avalonia.Thickness(5) };
            cancelButton.Click += (s, e) => { result = MessageBoxResult.Cancel; dialog.Close(); };
            buttonPanel.Children.Add(cancelButton);
        }
        else
        {
            var okButton = new Button { Content = "OK", Margin = new Avalonia.Thickness(5) };
            okButton.Click += (s, e) => { result = MessageBoxResult.OK; dialog.Close(); };
            buttonPanel.Children.Add(okButton);
        }

        panel.Children.Add(buttonPanel);
        dialog.Content = panel;
        
        await dialog.ShowDialog(owner);
        return result;
    }
}

public enum MessageBoxButtons
{
    OK,
    YesNoCancel
}

public enum MessageBoxIcon
{
    None,
    Information,
    Question,
    Warning,
    Error
}