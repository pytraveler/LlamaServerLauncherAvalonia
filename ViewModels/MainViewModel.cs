using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Resources;
using LlamaServerLauncher.Services;

namespace LlamaServerLauncher.ViewModels;

public class MainViewModel : INotifyPropertyChanged, IOnDemandProxyHost
{
    private bool _isInitializing = true;
    private readonly LlamaServerService _serverService;
    private ConfigurationService _configService;

    public ConfigurationService ConfigService => _configService;
    public Dictionary<string, DialogGeometry> DialogGeometryDict { get; } = new();
    private readonly LogService _logService;
    private readonly LlamaCppDownloadService _downloadService;
    private readonly DockerCliService _dockerService;
    private readonly AppUpdateService _appUpdateService = new();
    private readonly AutoStartService _autoStartService;
    private readonly DataPathResolver _dataPathResolver;
    private readonly LogStreamService _logStreamService;
    private readonly OnDemandProxyService _onDemandProxyService;
    private readonly Services.Optimization.HttpBenchmarkService _proxyHealth = new();
    private readonly System.Net.Http.HttpClient _comfyUiHttp = new() { Timeout = TimeSpan.FromSeconds(30) };
    private string _lastProxiedProfile = string.Empty;
    private readonly HashSet<string> _routingProfileNames = new(StringComparer.Ordinal);
    private ServerConfiguration? _loadedProfileConfig;
    private string _loadedProfileName = string.Empty;
    private string _llamaCppInstalledTag = "";
    private Dictionary<string, List<string>> _recentValues = new();
    private const int MaxRecentValues = 10;
    private Dictionary<string, string> _releaseBodyCache = new();
    private List<string> _releaseBodyCacheOrder = new();

    private DateTime _lastAppUpdateCheck;
    private DateTime _lastLlamaUpdateCheck;
    private int _appUpdateCheckInterval = 15;
    private int _llamaUpdateCheckInterval = 30;
    private List<ReleaseInfo> _cachedLlamaReleases = new();
    private DateTime _cachedLlamaReleasesTimestamp;
    private CancellationTokenSource? _periodicCheckCts;

    private bool _experimentalReposEnabled;
    private int _experimentalUpdateCheckInterval = 480;
    private DateTime _lastExperimentalUpdateCheck;
    private readonly ExperimentalRepoService _experimentalRepoService = new();

    private bool _scenariosEnabled;
    private string _selectedScenario = "";
    private bool _selectedScenarioAutoStart;
    private bool _isScenarioRunning;

    public ObservableCollection<ServerInstance> RunningInstances { get; } = new();
    private ServerInstance? _selectedInstance;

    public ObservableCollection<Models.ExperimentalRepoInfo> ExperimentalRepos { get; } = new();
    private Models.ExperimentalRepoInfo? _selectedExperimentalRepo;

    public ObservableCollection<string> Scenarios { get; } = new();

    public ServerInstance? SelectedInstance
    {
        get => _selectedInstance;
        set
        {
            if (_selectedInstance != value)
            {
                if (_selectedInstance != null)
                    _selectedInstance.IsSelected = false;

                _selectedInstance = value;

                if (_selectedInstance != null)
                    _selectedInstance.IsSelected = true;

                OnPropertyChanged();
                SyncPropertiesFromInstance();
            }
        }
    }

    private void SyncPropertiesFromInstance()
    {
        var inst = _selectedInstance;
        IsServerRunning = inst?.IsRunning ?? false;
        AutoRestart = inst?.AutoRestart ?? false;
        LogEnabled = inst?.LogEnabled ?? true;
        ShowServerStartError = inst?.ShowServerStartError ?? false;
        ServerStatus = inst != null
            ? (inst.IsRunning
                ? string.Format(Resources.LocalizedStrings.GetString("StatusRunning"), inst.ProcessId)
                : Localized.StatusStopped)
            : Localized.StatusStopped;
        UpdateCommandStates();
    }

    private void UpdateCommandStates()
    {
        OnPropertyChanged(nameof(CanStartServer));
        OnPropertyChanged(nameof(CanOpenInBrowser));
        if (StartServerCommand is AsyncRelayCommand startCmd)
            startCmd.RaiseCanExecuteChanged();
        if (StopServerCommand is AsyncRelayCommand stopCmd)
            stopCmd.RaiseCanExecuteChanged();
        if (RestartServerCommand is AsyncRelayCommand restartCmd)
            restartCmd.RaiseCanExecuteChanged();
        if (UnloadModelCommand is AsyncRelayCommand unloadCmd)
            unloadCmd.RaiseCanExecuteChanged();
        if (OpenInBrowserCommand is AsyncRelayCommand openCmd)
            openCmd.RaiseCanExecuteChanged();
    }

    public LogService LogService => _logService;
    public LocalizedStrings Localized { get; } = LocalizedStrings.Instance;

    public ToastService Toasts { get; } = new();
    
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

    private List<string> _cacheTypeOptions = new() { "", "f32", "f16", "bf16", "q8_0", "q4_0", "q4_1", "iq4_nl", "q5_0", "q5_1", "turbo2", "turbo3", "turbo4" };
    private List<string> _validCacheTypeValues = new(); // only values from help, for command line validation
    private bool _suppressCacheTypeKChange;
    private bool _suppressCacheTypeVChange;

    public List<string> CacheTypeOptions => _cacheTypeOptions;

    public List<string> DockerImageOptions { get; } = new()
    {
        "ghcr.io/ggml-org/llama.cpp:server",
        "ghcr.io/ggml-org/llama.cpp:server-cuda",
        "ghcr.io/ggml-org/llama.cpp:server-cuda13",
        "ghcr.io/ggml-org/llama.cpp:server-rocm",
        "ghcr.io/ggml-org/llama.cpp:server-vulkan",
        "ghcr.io/ggml-org/llama.cpp:server-intel",
        "ghcr.io/ggml-org/llama.cpp:server-musa",
        "ghcr.io/ggml-org/llama.cpp:server-openvino",
        "ghcr.io/ggml-org/llama.cpp:server-s390x"
    };

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

    private string _themeVariant = "Dark";
    public string ThemeVariant
    {
        get => _themeVariant;
        set
        {
            if (_themeVariant != value && value != null)
            {
                _themeVariant = value;
                OnPropertyChanged();
                // Default custom colors depend on the theme variant; refresh so the
                // swatches don't show stale defaults.
                RefreshCustomColorProperties();
                ApplyTheme();
            }
        }
    }

    public List<string> ThemeOptions { get; } = new() { "Dark", "Light" };

    public record ColorSchemeOption
    {
        public string Key { get; init; } = "";
        public string DisplayName { get; init; } = "";
    }

    private string _colorScheme = "Default";
    private readonly Dictionary<string, string> _customColors = new();
    private DispatcherTimer? _customColorSaveTimer;

    private static readonly Dictionary<string, string> DarkCustomDefaults = new()
    {
        { "WindowBackgroundBrush", "#1E1E1E" },
        { "PanelBackgroundBrush", "#252526" },
        { "CommandBackgroundBrush", "#2D2D30" },
        { "AccentForegroundBrush", "#4EC9B0" },
        { "SeparatorBrush", "#333333" }
    };
    private static readonly Dictionary<string, string> LightCustomDefaults = new()
    {
        { "WindowBackgroundBrush", "#E8E8E8" },
        { "PanelBackgroundBrush", "#F3F3F3" },
        { "CommandBackgroundBrush", "#E5E5E5" },
        { "AccentForegroundBrush", "#007ACC" },
        { "SeparatorBrush", "#CCCCCC" }
    };

    public string ColorScheme
    {
        get => _colorScheme;
        set
        {
            if (_colorScheme != value && value != null)
            {
                _colorScheme = value;
                if (value == "Custom")
                    EnsureCustomDefaultsSeeded();
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCustomScheme));
                RefreshCustomColorProperties();
                ApplyTheme();
            }
        }
    }

    public bool IsCustomScheme => _colorScheme == "Custom";

    public List<ColorSchemeOption> ColorSchemeOptions { get; } = new();

    public ICommand ResetCustomColorsCommand { get; }

    public Color CustomWindowBackground
    {
        get => GetCustomColor("WindowBackgroundBrush");
        set => SetCustomColor("WindowBackgroundBrush", nameof(CustomWindowBackground), value);
    }
    public Color CustomPanelBackground
    {
        get => GetCustomColor("PanelBackgroundBrush");
        set => SetCustomColor("PanelBackgroundBrush", nameof(CustomPanelBackground), value);
    }
    public Color CustomCommandBackground
    {
        get => GetCustomColor("CommandBackgroundBrush");
        set => SetCustomColor("CommandBackgroundBrush", nameof(CustomCommandBackground), value);
    }
    public Color CustomAccent
    {
        get => GetCustomColor("AccentForegroundBrush");
        set => SetCustomColor("AccentForegroundBrush", nameof(CustomAccent), value);
    }
    public Color CustomSeparator
    {
        get => GetCustomColor("SeparatorBrush");
        set => SetCustomColor("SeparatorBrush", nameof(CustomSeparator), value);
    }

    private void EnsureCustomDefaultsSeeded()
    {
        var defaults = _themeVariant == "Light" ? LightCustomDefaults : DarkCustomDefaults;
        foreach (var kv in defaults)
            _customColors.TryAdd(kv.Key, kv.Value);
    }

    private Color GetCustomColor(string key)
    {
        if (_customColors.TryGetValue(key, out var hex) && !string.IsNullOrWhiteSpace(hex)
            && Color.TryParse(hex, out var parsed))
            return parsed;
        var defaults = _themeVariant == "Light" ? LightCustomDefaults : DarkCustomDefaults;
        return defaults.TryGetValue(key, out var defHex) && Color.TryParse(defHex, out var defParsed)
            ? defParsed
            : Colors.Black;
    }

    private void SetCustomColor(string key, string propertyName, Color color)
    {
        var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        _customColors[key] = hex;
        OnPropertyChanged(propertyName);
        if (IsCustomScheme && !_isInitializing)
            App.ApplyCustomColor(key, hex);
        ScheduleCustomColorSave();
    }

    private void RefreshCustomColorProperties()
    {
        OnPropertyChanged(nameof(CustomWindowBackground));
        OnPropertyChanged(nameof(CustomPanelBackground));
        OnPropertyChanged(nameof(CustomCommandBackground));
        OnPropertyChanged(nameof(CustomAccent));
        OnPropertyChanged(nameof(CustomSeparator));
    }

    private void ResetCustomColors()
    {
        var defaults = _themeVariant == "Light" ? LightCustomDefaults : DarkCustomDefaults;
        _customColors.Clear();
        foreach (var kv in defaults)
            _customColors[kv.Key] = kv.Value;
        RefreshCustomColorProperties();
        ApplyTheme();
        ScheduleCustomColorSave();
    }

    private void ScheduleCustomColorSave()
    {
        if (_isInitializing) return;
        if (_customColorSaveTimer == null)
        {
            _customColorSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _customColorSaveTimer.Tick += (_, _) =>
            {
                _customColorSaveTimer!.Stop();
                _ = SaveSettingsAsync();
            };
        }
        _customColorSaveTimer.Stop();
        _customColorSaveTimer.Start();
    }

    private void RebuildColorSchemeOptions()
    {
        ColorSchemeOptions.Clear();
        ColorSchemeOptions.Add(new ColorSchemeOption { Key = "Default", DisplayName = LocalizedStrings.Instance.ColorSchemeDefault });
        ColorSchemeOptions.Add(new ColorSchemeOption { Key = "Ubuntu", DisplayName = LocalizedStrings.Instance.ColorSchemeUbuntu });
        ColorSchemeOptions.Add(new ColorSchemeOption { Key = "Ocean", DisplayName = LocalizedStrings.Instance.ColorSchemeOcean });
        ColorSchemeOptions.Add(new ColorSchemeOption { Key = "Forest", DisplayName = LocalizedStrings.Instance.ColorSchemeForest });
        ColorSchemeOptions.Add(new ColorSchemeOption { Key = "Sunset", DisplayName = LocalizedStrings.Instance.ColorSchemeSunset });
        ColorSchemeOptions.Add(new ColorSchemeOption { Key = "Custom", DisplayName = LocalizedStrings.Instance.ColorSchemeCustom });
    }

    private void OnCultureChanged()
    {
        RebuildColorSchemeOptions();
        OnPropertyChanged(nameof(ColorSchemeOptions));
        OnPropertyChanged(nameof(ColorScheme));
    }

    /// <summary>Unsubscribes from things the view model hooked into at startup.</summary>
    public void Cleanup()
    {
        LocalizedStrings.CultureChanged -= OnCultureChanged;
        _customColorSaveTimer?.Stop();
        _onDemandProxyService?.Dispose();
    }

    public List<string> AvailableFonts { get; } = GetSystemFonts();

    private static List<string> GetSystemFonts()
    {
        try
        {
            var fonts = FontManager.Current.SystemFonts
                .Select(f => f.Name)
                .Distinct()
                .OrderBy(n => n)
                .ToList();
            fonts.Insert(0, "");
            return fonts;
        }
        catch
        {
            return new List<string> { "", "Inter" };
        }
    }
    private string _selectedFontFamily = "";
    public string SelectedFontFamily
    {
        get => _selectedFontFamily;
        set
        {
            if (_selectedFontFamily != value)
            {
                _selectedFontFamily = value ?? "";
                OnPropertyChanged();
                OnPropertyChanged(nameof(EffectiveFontFamily));
            }
        }
    }

    public FontFamily EffectiveFontFamily =>
        string.IsNullOrEmpty(_selectedFontFamily) ? FontFamily.Default : new FontFamily(_selectedFontFamily);

    public int AppUpdateCheckInterval
    {
        get => _appUpdateCheckInterval;
        set
        {
            var clamped = Math.Clamp(value, 1, 180);
            if (_appUpdateCheckInterval != clamped)
            {
                _appUpdateCheckInterval = clamped;
                _lastAppUpdateCheck = DateTime.Now;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowUpdateIntervalWarning));
                _ = SaveSettingsAsync();
            }
        }
    }

    public int LlamaUpdateCheckInterval
    {
        get => _llamaUpdateCheckInterval;
        set
        {
            var clamped = Math.Clamp(value, 1, 180);
            if (_llamaUpdateCheckInterval != clamped)
            {
                _llamaUpdateCheckInterval = clamped;
                _lastLlamaUpdateCheck = DateTime.Now;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowUpdateIntervalWarning));
                _ = SaveSettingsAsync();
            }
        }
    }

    public bool ShowUpdateIntervalWarning => _appUpdateCheckInterval < 10 || _llamaUpdateCheckInterval < 10;

    public bool ExperimentalReposEnabled
    {
        get => _experimentalReposEnabled;
        set
        {
            if (_experimentalReposEnabled != value)
            {
                _experimentalReposEnabled = value;
                OnPropertyChanged();
                _ = SaveSettingsAsync();
            }
        }
    }

    public int ExperimentalUpdateCheckInterval
    {
        get => _experimentalUpdateCheckInterval;
        set
        {
            var clamped = Math.Clamp(value, 1, 1440);
            if (_experimentalUpdateCheckInterval != clamped)
            {
                _experimentalUpdateCheckInterval = clamped;
                OnPropertyChanged();
                _ = SaveSettingsAsync();
            }
        }
    }

    public Models.ExperimentalRepoInfo? SelectedExperimentalRepo
    {
        get => _selectedExperimentalRepo;
        set
        {
            if (_selectedExperimentalRepo != value)
            {
                _selectedExperimentalRepo = value;
                OnPropertyChanged();
            }
        }
    }

    public List<ReleaseInfo> CachedLlamaReleases => _cachedLlamaReleases;
    public DateTime CachedLlamaReleasesTimestamp => _cachedLlamaReleasesTimestamp;

    private void ApplyTheme()
    {
        if (!_isInitializing)
            App.SwitchTheme(_themeVariant, _colorScheme, _colorScheme == "Custom" ? _customColors : null);
    }

    private void ChangeLanguage(string? languageCode)
    {
        if (string.IsNullOrEmpty(languageCode)) return;
        var culture = new CultureInfo(languageCode);
        LocalizedStrings.SetCulture(culture);
        OnPropertyChanged(nameof(Localized));
        OnPropertyChanged(nameof(ToggleLogButtonText));
        OnPropertyChanged(nameof(ToggleTabPanelButtonText));
        OnPropertyChanged(nameof(ExecutablePathPlaceholder));
        OnPropertyChanged(nameof(LlamaButtonText));
        OnPropertyChanged(nameof(ParallelSlotsPlaceholder));
        OnPropertyChanged(nameof(TimeoutPlaceholder));
        OnPropertyChanged(nameof(ReasoningBudgetPlaceholder));
        OnPropertyChanged(nameof(SeedPlaceholder));
        OnPropertyChanged(nameof(PresencePenaltyPlaceholder));
        OnPropertyChanged(nameof(FrequencyPenaltyPlaceholder));
        OnPropertyChanged(nameof(FeatureNotSupportedTooltip));
        // Placeholders — main tab
        OnPropertyChanged(nameof(ModelPathPlaceholder));
        OnPropertyChanged(nameof(ModelsDirPlaceholder));
        OnPropertyChanged(nameof(HostPlaceholder));
        OnPropertyChanged(nameof(PortPlaceholder));
        OnPropertyChanged(nameof(ContextSizePlaceholder));
        OnPropertyChanged(nameof(ThreadsPlaceholder));
        OnPropertyChanged(nameof(GpuLayersPlaceholder));
        OnPropertyChanged(nameof(CpuMoePlaceholder));
        OnPropertyChanged(nameof(TemperaturePlaceholder));
        OnPropertyChanged(nameof(MaxTokensPlaceholder));
        OnPropertyChanged(nameof(BatchSizePlaceholder));
        OnPropertyChanged(nameof(UBatchSizePlaceholder));
        OnPropertyChanged(nameof(MinPPlaceholder));
        OnPropertyChanged(nameof(TopKPlaceholder));
        OnPropertyChanged(nameof(TopPPlaceholder));
        OnPropertyChanged(nameof(RepeatPenaltyPlaceholder));
        OnPropertyChanged(nameof(ApiKeyPlaceholder));
        OnPropertyChanged(nameof(AliasPlaceholder));
        OnPropertyChanged(nameof(LogFilePathPlaceholder));
        OnPropertyChanged(nameof(MmprojPathPlaceholder));
        OnPropertyChanged(nameof(CustomArgumentsPlaceholder));
        // Placeholders — speculative tab
        OnPropertyChanged(nameof(SpecDraftNMaxPlaceholder));
        OnPropertyChanged(nameof(SpecDraftNMinPlaceholder));
        OnPropertyChanged(nameof(SpecDraftPSplitPlaceholder));
        OnPropertyChanged(nameof(SpecDraftPMinPlaceholder));
        OnPropertyChanged(nameof(SpecDraftGpuLayersPlaceholder));
        OnPropertyChanged(nameof(SpecDraftModelPlaceholder));
        // Placeholders — HF
        OnPropertyChanged(nameof(HfRepoPlaceholder));
        OnPropertyChanged(nameof(HfFilePlaceholder));
        OnPropertyChanged(nameof(HfRepoDraftPlaceholder));
        // Tooltips — speculative tab
        OnPropertyChanged(nameof(SpecTypeToolTip));
        OnPropertyChanged(nameof(SpecDraftNMaxToolTip));
        OnPropertyChanged(nameof(SpecDraftNMinToolTip));
        OnPropertyChanged(nameof(SpecDraftPSplitToolTip));
        OnPropertyChanged(nameof(SpecDraftPMinToolTip));
        OnPropertyChanged(nameof(DraftModelToolTip));
        OnPropertyChanged(nameof(DraftGpuLayersToolTip));
        // Tooltips — HF
        OnPropertyChanged(nameof(HfRepoToolTip));
        OnPropertyChanged(nameof(HfFileToolTip));
        OnPropertyChanged(nameof(OfflineToolTip));
        OnPropertyChanged(nameof(HfRepoDraftToolTip));
        // Tooltips — cache type
        OnPropertyChanged(nameof(CacheTypeKToolTip));
        OnPropertyChanged(nameof(CacheTypeVToolTip));
        // Unsupported warning (used by speculative and cache tooltips)
        OnPropertyChanged(nameof(UnsupportedArgWarningText));
        // DataPathTooltip uses Localized strings
        OnPropertyChanged(nameof(DataPathTooltip));
    }

    private string _executablePath = string.Empty;
    private string _modelPath = string.Empty;
    private string _modelsDir = string.Empty;
    private string _llamaCppCustomDownloadPath = string.Empty;
    private string _host = "127.0.0.1";
    private string _port = "8080";
    private string _contextSize = string.Empty;
    private string _threads = string.Empty;
    private string _gpuLayers = string.Empty;
    private string _cpuMoe = string.Empty;
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
    private string _parallelSlots = string.Empty;
    private bool? _contBatching;
    private string _timeout = string.Empty;
    private bool? _cachePrompt;
    private bool? _mlock;
    private bool? _mmap;
    private bool? _reasoning;
    private string _reasoningBudget = string.Empty;
    private string _seed = string.Empty;
    private string _presencePenalty = string.Empty;
    private string _frequencyPenalty = string.Empty;
    private bool? _contextShift;
    private string _specType = string.Empty;
    private string _specDraftModel = string.Empty;
    private string _specDraftGpuLayers = string.Empty;
    private string _specDraftNMax = string.Empty;
    private string _specDraftNMin = string.Empty;
    private string _specDraftPSplit = string.Empty;
    private string _specDraftPMin = string.Empty;
    private string _hfRepo = string.Empty;
    private string _hfFile = string.Empty;
    private bool _offline;
    private string _hfRepoDraft = string.Empty;
    private bool _runInDocker;
    private string _dockerImage = "ghcr.io/ggml-org/llama.cpp:server";
    private bool _dockerGpuAll;
    private bool _dockerRm = true;
    private string _dockerContainerName = string.Empty;
    private bool _isDockerAvailable;
    private HashSet<string>? _supportedFlags;
    private string _lastCheckedExePath = "";
    private string _lastHelpText = "";
    private List<string> _specTypeOptions = new() { "", "none", "draft-simple", "draft-mtp" };
    private List<string> _validSpecTypeValues = new(); // only values from help, for command line validation
    private bool _suppressSpecTypeChange; // prevents ComboBox from resetting SpecType during ItemsSource rebuild
    private bool _autoRestart;
    private bool _autoStartWithSystem;
    private string _customBrowserPath = string.Empty;
    private bool _confirmStopServer = true;
    private bool _autoScroll = true;
    private bool _logEnabled = true;
    private bool _logVisible = true;
    private bool _tabPanelVisible = true;
    private bool _isNavPaneOpen = true;
    private int _selectedTabIndex;
    private bool _autoFitHeight;
    private double _autoFitHeightSavedHeight = 650;
    private double _windowWidth = 900;
    private double _windowHeight = 650;
    private double? _windowLeft;
    private double? _windowTop;
    private string _selectedProfile = string.Empty;
    private string _profileNameInput = string.Empty;
    private bool _suppressProfileAutoLoad;
    private bool _isServerRunning;
    private string _serverStatus = "Stopped";
    private string _currentLog = string.Empty;
    private string _logOutput = string.Empty;
    private string _logText = string.Empty;
    private string _currentCommand = string.Empty;
    private bool _useDefaultDataPath = true;
    private bool _showServerStartError;
    private CancellationTokenSource? _errorAnimationCts;
    private readonly List<string> _pendingLogs = new();
    private bool _logFlushScheduled;

    private bool _logStreamEnabled;
    private int _logStreamPort = 5872;
    private string _logStreamToken = string.Empty;

    public bool LogStreamEnabled
    {
        get => _logStreamEnabled;
        set
        {
            if (_logStreamEnabled != value)
            {
                _logStreamEnabled = value;
                OnPropertyChanged();
                ApplyLogStreamState();
                _ = SaveSettingsAsync();
            }
        }
    }

    public int LogStreamPort
    {
        get => _logStreamPort;
        set
        {
            if (_logStreamPort != value)
            {
                _logStreamPort = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LogStreamStatusText));
                OnPropertyChanged(nameof(LogStreamUrlHint));
            }
        }
    }

    public string LogStreamToken
    {
        get => _logStreamToken;
        set
        {
            if (_logStreamToken != value)
            {
                _logStreamToken = value;
                OnPropertyChanged();
            }
        }
    }

    public bool LogStreamRunning => _logStreamService.IsRunning;

    public int LogStreamClientCount => _logStreamService.ConnectedClientCount;

    public string LogStreamStatusText
    {
        get
        {
            if (!_logStreamEnabled) return LocalizedStrings.GetString("LogStreamingStopped");
            if (_logStreamService.IsRunning)
                return string.Format(LocalizedStrings.GetString("LogStreamingRunning"), _logStreamPort);
            return LocalizedStrings.GetString("LogStreamingStopped");
        }
    }

    public string LogStreamUrlHint
    {
        get
        {
            try
            {
                var host = System.Net.Dns.GetHostName();
                return $"http://{host}:{_logStreamPort}";
            }
            catch
            {
                return $"http://<hostname>:{_logStreamPort}";
            }
        }
    }

    private void ApplyLogStreamState()
    {
        if (_logStreamEnabled)
        {
            try
            {
                _logStreamService.Start(_logStreamPort, _logStreamToken);
            }
            catch (Exception ex)
            {
                _logService.Error($"Failed to start log stream: {ex.Message}");
                _logStreamEnabled = false;
                OnPropertyChanged(nameof(LogStreamEnabled));
            }
        }
        else
        {
            _logStreamService.Stop();
        }
        OnPropertyChanged(nameof(LogStreamRunning));
        OnPropertyChanged(nameof(LogStreamStatusText));
        OnPropertyChanged(nameof(LogStreamUrlHint));
    }

    private async Task RestartLogStreamAsync()
    {
        _logStreamService.Stop();
        if (_logStreamEnabled)
        {
            try
            {
                _logStreamService.Start(_logStreamPort, _logStreamToken);
                _logService.AppLog($"Log stream restarted on port {_logStreamPort}");
            }
            catch (Exception ex)
            {
                _logService.Error($"Failed to restart log stream: {ex.Message}");
                _logStreamEnabled = false;
                OnPropertyChanged(nameof(LogStreamEnabled));
            }
        }
        OnPropertyChanged(nameof(LogStreamRunning));
        OnPropertyChanged(nameof(LogStreamStatusText));
        OnPropertyChanged(nameof(LogStreamUrlHint));
        await SaveSettingsAsync();
    }

    private bool _onDemandProxyEnabled;
    private int _onDemandProxyPort = 8081;
    private int _onDemandProxyIdleSeconds = 300;
    private int _onDemandProxyHealthTimeoutSeconds = 120;
    private string _onDemandProxyApiKey = string.Empty;
    private bool _onDemandProxyMiniWindowEnabled;
    private bool _comfyUiFreeEnabled;
    private string _comfyUiUrl = "http://127.0.0.1:8188";

    public bool OnDemandProxyEnabled
    {
        get => _onDemandProxyEnabled;
        set
        {
            if (_onDemandProxyEnabled != value)
            {
                _onDemandProxyEnabled = value;
                OnPropertyChanged();
                ApplyOnDemandProxyState();
                _ = SaveSettingsAsync();
            }
        }
    }

    public int OnDemandProxyPort
    {
        get => _onDemandProxyPort;
        set
        {
            if (_onDemandProxyPort != value)
            {
                _onDemandProxyPort = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OnDemandProxyStatusText));
                OnPropertyChanged(nameof(OnDemandProxyUrlHint));
            }
        }
    }

    public int OnDemandProxyIdleSeconds
    {
        get => _onDemandProxyIdleSeconds;
        set
        {
            if (_onDemandProxyIdleSeconds != value)
            {
                _onDemandProxyIdleSeconds = value;
                OnPropertyChanged();
            }
        }
    }

    public int OnDemandProxyHealthTimeoutSeconds
    {
        get => _onDemandProxyHealthTimeoutSeconds;
        set
        {
            if (_onDemandProxyHealthTimeoutSeconds != value)
            {
                _onDemandProxyHealthTimeoutSeconds = value;
                OnPropertyChanged();
            }
        }
    }

    public string OnDemandProxyApiKey
    {
        get => _onDemandProxyApiKey;
        set
        {
            if (_onDemandProxyApiKey != value)
            {
                _onDemandProxyApiKey = value;
                OnPropertyChanged();
            }
        }
    }

    public bool OnDemandProxyMiniWindowEnabled
    {
        get => _onDemandProxyMiniWindowEnabled;
        set
        {
            if (_onDemandProxyMiniWindowEnabled != value)
            {
                _onDemandProxyMiniWindowEnabled = value;
                OnPropertyChanged();
                _ = SaveSettingsAsync();
            }
        }
    }

    public MiniCountdownViewModel MiniCountdown { get; }

    public event Action? MiniWindowShowRequested;
    public event Action? MiniWindowHideRequested;

    private void OnProxyModelLoaded(string profile)
    {
        if (!_onDemandProxyMiniWindowEnabled) return;
        Dispatcher.UIThread.Post(() =>
        {
            MiniCountdown.SetLoaded(profile);
            MiniWindowShowRequested?.Invoke();
        });
    }

    private void OnProxyActivityChanged(ProxyActivity activity)
    {
        if (!_onDemandProxyMiniWindowEnabled) return;
        Dispatcher.UIThread.Post(() => MiniCountdown.Apply(activity));
    }

    private void OnProxyModelUnloaded()
    {
        Dispatcher.UIThread.Post(() => MiniWindowHideRequested?.Invoke());
    }

    private Task HoldModelLoadedAsync()
    {
        _onDemandProxyService.HoldLoaded();
        MiniCountdown.IsHeld = true;
        return Task.CompletedTask;
    }

    private Task UnloadModelNowAsync() => _onDemandProxyService.UnloadNowAsync();

    public bool ComfyUiFreeEnabled
    {
        get => _comfyUiFreeEnabled;
        set
        {
            if (_comfyUiFreeEnabled != value)
            {
                _comfyUiFreeEnabled = value;
                OnPropertyChanged();
                _ = SaveSettingsAsync();
            }
        }
    }

    public string ComfyUiUrl
    {
        get => _comfyUiUrl;
        set
        {
            if (_comfyUiUrl != value)
            {
                _comfyUiUrl = value;
                OnPropertyChanged();
            }
        }
    }

    public bool OnDemandProxyRunning => _onDemandProxyService.IsRunning;

    public string OnDemandProxyStatusText
    {
        get
        {
            if (!_onDemandProxyEnabled) return LocalizedStrings.GetString("OnDemandProxyStopped");
            if (_onDemandProxyService.IsRunning)
                return string.Format(LocalizedStrings.GetString("OnDemandProxyRunningStatus"), _onDemandProxyPort);
            return LocalizedStrings.GetString("OnDemandProxyStopped");
        }
    }

    public string OnDemandProxyUrlHint
    {
        get
        {
            try
            {
                var host = System.Net.Dns.GetHostName();
                return $"http://{host}:{_onDemandProxyPort}/v1";
            }
            catch
            {
                return $"http://<hostname>:{_onDemandProxyPort}/v1";
            }
        }
    }

    private void ApplyOnDemandProxyState()
    {
        if (_onDemandProxyEnabled)
        {
            try
            {
                _onDemandProxyService.Start(new ProxyOptions
                {
                    Port = _onDemandProxyPort,
                    IdleSeconds = _onDemandProxyIdleSeconds,
                    ApiKey = string.IsNullOrWhiteSpace(_onDemandProxyApiKey) ? null : _onDemandProxyApiKey,
                });
            }
            catch (Exception ex)
            {
                _logService.Error($"Failed to start on-demand proxy: {ex.Message}");
                _onDemandProxyEnabled = false;
                OnPropertyChanged(nameof(OnDemandProxyEnabled));
            }
        }
        else
        {
            _onDemandProxyService.Stop();
        }
        OnPropertyChanged(nameof(OnDemandProxyRunning));
        OnPropertyChanged(nameof(OnDemandProxyStatusText));
        OnPropertyChanged(nameof(OnDemandProxyUrlHint));
    }

    public void ApplyOnDemandProxyFromSettings()
    {
        if (_onDemandProxyEnabled)
            ApplyOnDemandProxyState();
        OnPropertyChanged(nameof(OnDemandProxyEnabled));
        OnPropertyChanged(nameof(OnDemandProxyPort));
        OnPropertyChanged(nameof(OnDemandProxyIdleSeconds));
        OnPropertyChanged(nameof(OnDemandProxyHealthTimeoutSeconds));
        OnPropertyChanged(nameof(OnDemandProxyApiKey));
        OnPropertyChanged(nameof(OnDemandProxyMiniWindowEnabled));
        OnPropertyChanged(nameof(ComfyUiFreeEnabled));
        OnPropertyChanged(nameof(ComfyUiUrl));
        OnPropertyChanged(nameof(OnDemandProxyStatusText));
        OnPropertyChanged(nameof(OnDemandProxyUrlHint));
    }

    private async Task RestartOnDemandProxyAsync()
    {
        _onDemandProxyService.Stop();
        if (_onDemandProxyEnabled)
            ApplyOnDemandProxyState();
        OnPropertyChanged(nameof(OnDemandProxyRunning));
        OnPropertyChanged(nameof(OnDemandProxyStatusText));
        OnPropertyChanged(nameof(OnDemandProxyUrlHint));
        await SaveSettingsAsync();
    }

    IReadOnlyList<string> IOnDemandProxyHost.GetProfileNames() =>
        Profiles.Where(p => !_routingProfileNames.Contains(p)).ToList();

    string? IOnDemandProxyHost.GetFallbackProfileName()
    {
        if (!string.IsNullOrWhiteSpace(_lastProxiedProfile)) return _lastProxiedProfile;
        if (!string.IsNullOrWhiteSpace(_loadedProfileName)) return _loadedProfileName;
        return SelectedProfile;
    }

    async Task<ProxyUpstream?> IOnDemandProxyHost.EnsureProfileRunningAsync(string profileName, CancellationToken ct)
    {
        try
        {
            var config = await _configService.LoadProfileAsync(profileName);
            if (config == null)
            {
                _logService.Error($"On-demand proxy: profile '{profileName}' not found");
                return null;
            }

            var declaredHost = string.IsNullOrWhiteSpace(config.Host) ? "127.0.0.1" : config.Host;
            var port = config.Port;
            var connectHost = declaredHost is "0.0.0.0" or "" ? "127.0.0.1" : declaredHost;
            var baseUrl = $"http://{connectHost}:{port}";

            var alreadyRunning = RunningInstances.Any(i => i.ProfileName == profileName && i.IsRunning);
            if (!alreadyRunning)
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var others = RunningInstances.Where(i => i.IsRunning && i.ProfileName != profileName).ToList();
                    foreach (var other in others)
                    {
                        try { await other.StopAsync(); }
                        catch (Exception ex) { _logService.Error($"On-demand proxy: failed to stop '{other.ProfileName}': {ex.Message}"); }
                    }

                    await LaunchInstanceAsync(profileName, config);
                });
            }

            var ready = await _proxyHealth.WaitForHealthAsync(baseUrl, _onDemandProxyHealthTimeoutSeconds, ct);
            if (!ready)
            {
                _logService.Error($"On-demand proxy: profile '{profileName}' did not become healthy in {_onDemandProxyHealthTimeoutSeconds}s");
                return null;
            }

            _lastProxiedProfile = profileName;
            return new ProxyUpstream(declaredHost, port);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logService.Error($"On-demand proxy: ensure '{profileName}' failed: {ex.Message}");
            return null;
        }
    }

    async Task IOnDemandProxyHost.StopActiveAsync()
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var targets = RunningInstances
                    .Where(i => i.IsRunning && (string.IsNullOrEmpty(_lastProxiedProfile) || i.ProfileName == _lastProxiedProfile))
                    .ToList();
                foreach (var instance in targets)
                {
                    try { await instance.StopAsync(); }
                    catch (Exception ex) { _logService.Error($"On-demand proxy: idle stop of '{instance.ProfileName}' failed: {ex.Message}"); }
                }
            });
        }
        catch (Exception ex)
        {
            _logService.Error($"On-demand proxy: idle stop failed: {ex.Message}");
        }
    }

    private async Task FreeComfyUiIfEnabledAsync()
    {
        if (!_comfyUiFreeEnabled || string.IsNullOrWhiteSpace(_comfyUiUrl)) return;
        try
        {
            var url = _comfyUiUrl.TrimEnd('/') + "/free";
            using var content = new System.Net.Http.StringContent(
                "{\"unload_models\":true,\"free_memory\":true}",
                System.Text.Encoding.UTF8,
                "application/json");
            using var resp = await _comfyUiHttp.PostAsync(url, content);
            _logService.AppLog($"ComfyUI: /free -> {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            _logService.Error($"ComfyUI: /free failed: {ex.Message}");
        }
    }

    public Func<string, string, Task<bool>>? ConfirmActionFunc { get; set; }

    public Func<string, string, string, Task> ShowMessageFunc { get; set; } = (_, _, _) => Task.CompletedTask;

    public Func<string, Task<string?>>? BrowseFolderFunc { get; set; }

    private readonly HashSet<string> _shownConflictToasts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _shownCorruptFileToasts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _pendingCorruptFileToasts = new();

    private ToastItem? _autoFitHeightToast;
    // Show the auto-fit warning at most once per session.
    private bool _autoFitHeightWarningShown;

    private bool _isLoadingConfig;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel()
    {
        _dataPathResolver = new DataPathResolver();
        var resolvedPath = _dataPathResolver.ResolveDataPath();

        _logService = new LogService(resolvedPath);
        _logService.LogReceived += OnLogReceived;
        if (_dataPathResolver.ConfiguredCustomPathMissing)
            _logService.Warning("Configured custom data folder is unavailable; using the default folder. Settings may appear reset until the drive is reconnected.");
        _serverService = new LlamaServerService(_logService);
        _configService = new ConfigurationService(_logService, resolvedPath);
        _configService.CorruptFileSkipped += OnCorruptFileSkipped;
        _downloadService = new LlamaCppDownloadService(resolvedPath);
        _dockerService = new DockerCliService(_logService);
        _autoStartService = new AutoStartService(_logService);
        _logStreamService = new LogStreamService(_logService);
        _logStreamService.ClientCountChanged += () =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(LogStreamClientCount));
                OnPropertyChanged(nameof(LogStreamStatusText));
            });
        };
        _onDemandProxyService = new OnDemandProxyService(_logService, this);
        MiniCountdown = new MiniCountdownViewModel(HoldModelLoadedAsync, UnloadModelNowAsync);
        _onDemandProxyService.ModelLoaded += OnProxyModelLoaded;
        _onDemandProxyService.ActivityChanged += OnProxyActivityChanged;
        _onDemandProxyService.ModelUnloaded += OnProxyModelUnloaded;

        RunningInstances.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasAnyRunningInstances));
            OnPropertyChanged(nameof(HasAnyInstances));
            RequestTrayMenuRebuild?.Invoke();
        };

        CustomArgumentItems.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasCustomArgumentItems));
        };

        Profiles = new ObservableCollection<string>();

        RebuildColorSchemeOptions();
        ResetCustomColorsCommand = new RelayCommand(ResetCustomColors);
        LocalizedStrings.CultureChanged += OnCultureChanged;

        ChangeLanguage(_selectedLanguage);

        BrowseExecutableCommand = new AsyncRelayCommand(BrowseExecutableAsync);
        BrowseModelCommand = new AsyncRelayCommand(BrowseModelAsync);
        BrowseModelsDirCommand = new AsyncRelayCommand(BrowseModelsDirAsync);
        BrowseLogFileCommand = new AsyncRelayCommand(BrowseLogFileAsync);
        BrowseMmprojCommand = new AsyncRelayCommand(BrowseMmprojAsync);
        BrowseDraftModelCommand = new AsyncRelayCommand(BrowseDraftModelAsync);
        BrowseBrowserCommand = new AsyncRelayCommand(BrowseBrowserAsync);
        StartServerCommand = new AsyncRelayCommand(StartServerAsync, () => CanStartServer);
        RestartServerCommand = new AsyncRelayCommand(RestartServerAsync, () => IsServerRunning);
        StopServerCommand = new AsyncRelayCommand(StopServerAsync, () => IsServerRunning || (_selectedInstance?.IsRunning ?? false));
        UnloadModelCommand = new AsyncRelayCommand(UnloadModelAsync, () => IsServerRunning);
        OpenInBrowserCommand = new AsyncRelayCommand(OpenInBrowserAsync, () => CanOpenInBrowser);
        SaveProfileCommand = new AsyncRelayCommand(SaveProfileAsync);
        SaveProfileAsCommand = new AsyncRelayCommand(SaveProfileAsAsync);
        DeleteProfileCommand = new AsyncRelayCommand(DeleteProfileAsync);
        RenameProfileCommand = new AsyncRelayCommand(RenameProfileAsync);
        CloneProfileCommand = new AsyncRelayCommand(CloneProfileAsync);
        ExportProfileCommand = new AsyncRelayCommand(ExportProfileAsync);
        ExportToBatCommand = new AsyncRelayCommand(ExportToBatAsync);
        ImportProfileCommand = new AsyncRelayCommand(ImportProfileAsync);
        ExportAllCommand = new AsyncRelayCommand(ExportAllProfilesAsync);
        ImportAllCommand = new AsyncRelayCommand(ImportAllProfilesAsync);
        ClearAllFieldsCommand = new RelayCommand(ClearAllFields);
        ClearLogCommand = new RelayCommand(ClearLog);
        CopyLogCommand = new RelayCommand(CopyLog);
        SaveLogCommand = new AsyncRelayCommand(SaveLogAsync);
        OpenArgumentPickerCommand = new AsyncRelayCommand(OpenArgumentPickerAsync);
        OpenOptimizerCommand = new RelayCommand(_ => OpenOptimizer());
        ShowWindowCommand = new RelayCommand(_ => { });
        CloseFromTrayCommand = new RelayCommand(async _ => { });
        RestartLogStreamCommand = new AsyncRelayCommand(RestartLogStreamAsync);
        RestartOnDemandProxyCommand = new AsyncRelayCommand(RestartOnDemandProxyAsync);

        RunScenarioCommand = new AsyncRelayCommand(RunScenarioAsync, () => HasSelectedScenario);
        EditScenarioCommand = new AsyncRelayCommand(EditScenarioAsync);
        CreateScenarioCommand = new AsyncRelayCommand(CreateScenarioAsync);
        DeleteScenarioCommand = new AsyncRelayCommand(DeleteScenarioAsync, () => HasSelectedScenario);

        LoadProfiles();
        _logService.AppLog($"Application started ({AppInfo.Version})");
        _useDefaultDataPath = !_dataPathResolver.IsCustomPathActive();
        UpdateCurrentCommand();
    }

    public async Task InitializeAsync()
    {
        var settings = await _configService.LoadAppSettingsAsync();
        await ApplyAppSettingsAsync(settings);
        _ = LoadDetectedBrowsersAsync();
        _logService.Configure(settings.MaxLogFiles, settings.MaxLogSizeBytes);
        _ = CheckDefaultLlamaVersionAsync();
        StartPeriodicUpdateChecks();
        await RefreshSupportedFlagsAsync();
        _ = CheckDockerAvailabilityAsync();
        ApplyLogStreamFromSettings();
        ApplyOnDemandProxyFromSettings();

        // Sticky warning when the configured data folder is unreachable - settings
        // would otherwise look "reset" because we silently fall back to defaults.
        if (_dataPathResolver.ConfiguredCustomPathMissing)
            Toasts.ShowError(Localized.ToastCustomDataPathMissing, durationMs: 0);

        // Flush the corrupt-file toasts queued during startup now that the language is set.
        if (_pendingCorruptFileToasts.Count > 0)
        {
            foreach (var fullPath in _pendingCorruptFileToasts)
                ShowCorruptFileToast(fullPath);
            _pendingCorruptFileToasts.Clear();
        }
    }

    public async Task CheckDockerAvailabilityAsync()
    {
        try
        {
            IsDockerAvailable = await _dockerService.IsDockerInstalledAsync();
        }
        catch
        {
            IsDockerAvailable = false;
        }
        finally
        {
            OnPropertyChanged(nameof(CanStartServer));
            if (StartServerCommand is AsyncRelayCommand startCmd)
                startCmd.RaiseCanExecuteChanged();
        }
    }

    public async Task ApplyAppSettingsAsync(AppSettings settings)
    {
        _windowWidth = settings.WindowWidth > 0 ? settings.WindowWidth : 900;
        _windowHeight = settings.WindowHeight > 0 ? settings.WindowHeight : 650;
        _windowLeft = settings.WindowLeft;
        _windowTop = settings.WindowTop;

        var language = string.IsNullOrEmpty(settings.Language) ? "en" : settings.Language;
        
        _selectedLanguage = language;
        ChangeLanguage(language);
        OnPropertyChanged(nameof(SelectedLanguage));
        
        ProfileNameInput = settings.ProfileNameInput;
        ExecutablePath = ResolveExecutablePath(settings.ExecutablePath);
        ModelPath = settings.ModelPath;
        ModelsDir = settings.ModelsDir;
        LlamaCppCustomDownloadPath = settings.LlamaCppCustomDownloadPath ?? "";
        Host = settings.Host;
        Port = settings.Port.ToString();
        ContextSize = settings.ContextSize;
        Threads = settings.Threads;
        GpuLayers = settings.GpuLayers;
        CpuMoe = settings.CpuMoe;
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
        ParallelSlots = settings.ParallelSlots;
        ContBatching = settings.ContBatching;
        Timeout = settings.Timeout;
        CachePrompt = settings.CachePrompt;
        Mlock = settings.Mlock;
        Mmap = settings.Mmap;
        Reasoning = settings.Reasoning;
        ReasoningBudget = settings.ReasoningBudget;
        Seed = settings.Seed;
        PresencePenalty = settings.PresencePenalty;
        FrequencyPenalty = settings.FrequencyPenalty;
        ContextShift = settings.ContextShift;
        SpecType = settings.SpecType;
        SpecDraftModel = settings.SpecDraftModel;
        SpecDraftGpuLayers = settings.SpecDraftGpuLayers;
        SpecDraftNMax = settings.SpecDraftNMax;
        SpecDraftNMin = settings.SpecDraftNMin;
        SpecDraftPSplit = settings.SpecDraftPSplit;
        SpecDraftPMin = settings.SpecDraftPMin;
        HfRepo = settings.HfRepo;
        HfFile = settings.HfFile;
        Offline = settings.Offline;
        HfRepoDraft = settings.HfRepoDraft;
        AutoRestart = settings.AutoRestart;
        ConfirmStopServer = settings.ConfirmStopServer;
        AutoStartWithSystem = _autoStartService.IsAutoStartEnabled();
        CustomBrowserPath = settings.CustomBrowserPath ?? "";
        AutoScroll = settings.AutoScrollLog;
        LogEnabled = settings.LogEnabled;
        LogVisible = settings.LogVisible;
        TabPanelVisible = settings.TabPanelVisible;
        IsNavPaneOpen = settings.IsNavPaneOpen;
        AutoFitHeight = settings.AutoFitHeight;
        AutoFitHeightSavedHeight = settings.AutoFitHeightSavedHeight > 0 ? settings.AutoFitHeightSavedHeight : 650;
        LogHeight = settings.LogHeight > 0 ? settings.LogHeight : 200;
        FontSizeLevel = string.IsNullOrEmpty(settings.FontSizeLevel) ? "Medium" : settings.FontSizeLevel;
        ThemeVariant = string.IsNullOrEmpty(settings.ThemeVariant) ? "Dark" : settings.ThemeVariant;
        _customColors.Clear();
        if (settings.CustomColors != null)
        {
            foreach (var kv in settings.CustomColors)
                if (!string.IsNullOrWhiteSpace(kv.Value))
                    _customColors[kv.Key] = kv.Value;
        }
        ColorScheme = string.IsNullOrEmpty(settings.ColorScheme) ? "Default" : settings.ColorScheme;
        SelectedFontFamily = settings.FontFamily ?? "";
        _llamaCppInstalledTag = settings.LlamaCppInstalledTag ?? "";
        SelectedTabIndex = settings.SelectedTabIndex;
        RunInDocker = settings.RunInDocker;
        DockerImage = string.IsNullOrEmpty(settings.DockerImage) ? "ghcr.io/ggml-org/llama.cpp:server" : settings.DockerImage;
        DockerGpuAll = settings.DockerGpuAll;
        DockerRm = settings.DockerRm;
        DockerContainerName = settings.DockerContainerName ?? "";
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
        _isInitializing = false;
        _recentValues = settings.RecentValuesHistory ?? new Dictionary<string, List<string>>();
        _releaseBodyCache = settings.ReleaseBodyCache ?? new Dictionary<string, string>();
        _releaseBodyCacheOrder = settings.ReleaseBodyCacheOrder ?? new List<string>();

        _lastAppUpdateCheck = settings.LastAppUpdateCheck;
        _lastLlamaUpdateCheck = settings.LastLlamaUpdateCheck;
        _appUpdateCheckInterval = Math.Clamp(settings.AppUpdateCheckIntervalMinutes, 1, 180);
        _llamaUpdateCheckInterval = Math.Clamp(settings.LlamaUpdateCheckIntervalMinutes, 1, 180);
        OnPropertyChanged(nameof(AppUpdateCheckInterval));
        OnPropertyChanged(nameof(LlamaUpdateCheckInterval));
        OnPropertyChanged(nameof(ShowUpdateIntervalWarning));
        if (!string.IsNullOrEmpty(settings.CachedLlamaReleasesJson))
        {
            try
            {
                _cachedLlamaReleases = System.Text.Json.JsonSerializer.Deserialize<List<ReleaseInfo>>(settings.CachedLlamaReleasesJson) ?? new List<ReleaseInfo>();
            }
            catch { _cachedLlamaReleases = new List<ReleaseInfo>(); }
        }
        else
        {
            _cachedLlamaReleases = new List<ReleaseInfo>();
        }
        _cachedLlamaReleasesTimestamp = settings.CachedLlamaReleasesTimestamp;

        if (_cachedLlamaReleases.Count > 0 && !string.IsNullOrEmpty(_llamaCppInstalledTag))
        {
            var latestCached = _cachedLlamaReleases[0];
            if (latestCached.Tag != _llamaCppInstalledTag)
            {
                IsLlamaUpdateAvailable = true;
                CacheReleaseBody(latestCached.Tag, latestCached.Body);
                var cleaned = CleanReleaseBody(latestCached.Body);
                var desc = cleaned.Length > 300 ? cleaned[..300] + "..." : cleaned;
                var currentVersionStr = string.Format(LocalizedStrings.GetString("CurrentVersionTooltip"), _llamaCppInstalledTag);
                LlamaUpdateTooltip = $"{currentVersionStr}\n\n{latestCached.Tag}\n{latestCached.PublishedAt:yyyy-MM-dd HH:mm}\n{desc}";
            }
        }

        ApplyTheme();

        _logStreamPort = settings.LogStreamPort > 0 ? settings.LogStreamPort : 5872;
        _logStreamToken = settings.LogStreamToken ?? "";
        _logStreamEnabled = settings.LogStreamEnabled;

        _onDemandProxyPort = settings.OnDemandProxyPort > 0 ? settings.OnDemandProxyPort : 8081;
        _onDemandProxyIdleSeconds = settings.OnDemandProxyIdleSeconds;
        _onDemandProxyHealthTimeoutSeconds = settings.OnDemandProxyHealthTimeoutSeconds > 0 ? settings.OnDemandProxyHealthTimeoutSeconds : 120;
        _onDemandProxyApiKey = settings.OnDemandProxyApiKey ?? "";
        _onDemandProxyMiniWindowEnabled = settings.OnDemandProxyMiniWindowEnabled;
        _onDemandProxyEnabled = settings.OnDemandProxyEnabled;
        _comfyUiFreeEnabled = settings.ComfyUiFreeEnabled;
        _comfyUiUrl = string.IsNullOrWhiteSpace(settings.ComfyUiUrl) ? "http://127.0.0.1:8188" : settings.ComfyUiUrl;

        ScenariosEnabled = settings.ScenariosEnabled;
        await LoadScenariosAsync();
        SelectedScenario = settings.SelectedScenario ?? "";
        UpdateSelectedScenarioAutoStart();

        // Load dialog geometry into in-memory dictionary
        DialogGeometryDict.Clear();
        if (settings.DialogGeometry != null)
        {
            foreach (var kv in settings.DialogGeometry)
                DialogGeometryDict[kv.Key] = kv.Value;
        }

        _experimentalReposEnabled = settings.ExperimentalReposEnabled;
        _experimentalUpdateCheckInterval = Math.Clamp(settings.ExperimentalUpdateCheckIntervalMinutes, 1, 1440);
        _lastExperimentalUpdateCheck = settings.LastExperimentalUpdateCheck;
        OnPropertyChanged(nameof(ExperimentalReposEnabled));
        OnPropertyChanged(nameof(ExperimentalUpdateCheckInterval));

        ExperimentalRepos.Clear();
        if (settings.ExperimentalRepos != null)
        {
            foreach (var repo in settings.ExperimentalRepos)
                ExperimentalRepos.Add(repo);
        }
    }

    public void ApplyLogStreamFromSettings()
    {
        if (_logStreamEnabled)
            ApplyLogStreamState();
        OnPropertyChanged(nameof(LogStreamEnabled));
        OnPropertyChanged(nameof(LogStreamPort));
        OnPropertyChanged(nameof(LogStreamToken));
        OnPropertyChanged(nameof(LogStreamStatusText));
        OnPropertyChanged(nameof(LogStreamUrlHint));
    }

    public bool UseDefaultDataPath
    {
        get => _useDefaultDataPath;
        set
        {
            if (_useDefaultDataPath != value)
            {
                _useDefaultDataPath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DataPathTooltip));
            }
        }
    }

    public string CurrentDataPath => _dataPathResolver.ResolveDataPath();

    public string DataPathTooltip
    {
        get
        {
            var path = CurrentDataPath;
            if (_useDefaultDataPath)
                return string.Format(Localized.DataPathTooltipDefault, path);
            return string.Format(Localized.DataPathTooltipCustom, path);
        }
    }

    public async Task ToggleDataPathAsync(bool useDefault)
    {
        if (useDefault)
        {
            var defaultPath = _dataPathResolver.DefaultAppDataPath;
            var msg = string.Format(Localized.ConfirmMoveToDefault, defaultPath);
            if (ConfirmActionFunc != null)
            {
                var confirmed = await ConfirmActionFunc(Localized.ConfirmTitle, msg);
                if (!confirmed)
                {
                    OnPropertyChanged(nameof(UseDefaultDataPath));
                    return;
                }
            }

            var currentPath = _dataPathResolver.ResolveDataPath();
            var error = await MigrateDataAsync(currentPath, defaultPath);
            if (error != null)
            {
                if (ShowMessageFunc != null)
                    await ShowMessageFunc(Localized.ErrorTitle, string.Format(Localized.MigrationError, error), "error");
                UseDefaultDataPath = false;
                return;
            }

            _dataPathResolver.ClearCustomPath();
            ReinitializeServices(defaultPath);
            UseDefaultDataPath = true;
            OnPropertyChanged(nameof(CurrentDataPath));
            OnPropertyChanged(nameof(DataPathTooltip));

            if (ShowMessageFunc != null)
                await ShowMessageFunc(Localized.SuccessTitle, string.Format(Localized.MigrationSuccess, defaultPath), "info");
        }
        else
        {
            if (BrowseFolderFunc == null)
            {
                UseDefaultDataPath = true;
                return;
            }

            var selectedPath = await BrowseFolderFunc(Localized.SelectDataDirectory);
            if (string.IsNullOrEmpty(selectedPath))
            {
                UseDefaultDataPath = true;
                return;
            }

            var msg = string.Format(Localized.ConfirmMoveToCustom, selectedPath);
            if (ConfirmActionFunc != null)
            {
                var confirmed = await ConfirmActionFunc(Localized.ConfirmTitle, msg);
                if (!confirmed)
                {
                    UseDefaultDataPath = true;
                    return;
                }
            }

            var currentPath = _dataPathResolver.ResolveDataPath();
            var error = await MigrateDataAsync(currentPath, selectedPath);
            if (error != null)
            {
                if (ShowMessageFunc != null)
                    await ShowMessageFunc(Localized.ErrorTitle, string.Format(Localized.MigrationError, error), "error");
                UseDefaultDataPath = true;
                return;
            }

            _dataPathResolver.SetCustomPath(selectedPath);
            ReinitializeServices(selectedPath);
            UseDefaultDataPath = false;
            OnPropertyChanged(nameof(CurrentDataPath));
            OnPropertyChanged(nameof(DataPathTooltip));

            if (ShowMessageFunc != null)
                await ShowMessageFunc(Localized.SuccessTitle, string.Format(Localized.MigrationSuccess, selectedPath), "info");
        }
    }

    public async Task ChangeCustomDataPathAsync()
    {
        if (BrowseFolderFunc == null) return;

        var selectedPath = await BrowseFolderFunc(Localized.SelectDataDirectory);
        if (string.IsNullOrEmpty(selectedPath)) return;

        var currentPath = _dataPathResolver.ResolveDataPath();
        if (string.Equals(
            Path.GetFullPath(selectedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(currentPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase))
            return;

        var msg = string.Format(Localized.ConfirmMoveToCustom, selectedPath);
        if (ConfirmActionFunc != null)
        {
            var confirmed = await ConfirmActionFunc(Localized.ConfirmTitle, msg);
            if (!confirmed) return;
        }

        var error = await MigrateDataAsync(currentPath, selectedPath);
        if (error != null)
        {
            if (ShowMessageFunc != null)
                await ShowMessageFunc(Localized.ErrorTitle, string.Format(Localized.MigrationError, error), "error");
            return;
        }

        _dataPathResolver.SetCustomPath(selectedPath);
        ReinitializeServices(selectedPath);
        OnPropertyChanged(nameof(CurrentDataPath));
        OnPropertyChanged(nameof(DataPathTooltip));

        if (ShowMessageFunc != null)
            await ShowMessageFunc(Localized.SuccessTitle, string.Format(Localized.MigrationSuccess, selectedPath), "info");
    }

    private void ReinitializeServices(string appDataPath)
    {
        _configService = new ConfigurationService(_logService, appDataPath);
        _configService.CorruptFileSkipped += OnCorruptFileSkipped;
        LoadProfiles();
    }

    private void OnCorruptFileSkipped(string fullPath)
    {
        // Don't re-toast the same file on every refresh.
        if (!_shownCorruptFileToasts.Add(fullPath)) return;

        // The language isn't applied yet during startup (LoadProfiles runs in the
        // constructor), so queue toasts and show them once the UI is ready.
        if (_isInitializing)
            _pendingCorruptFileToasts.Add(fullPath);
        else
            ShowCorruptFileToast(fullPath);
    }

    private void ShowCorruptFileToast(string fullPath)
    {
        var fileName = System.IO.Path.GetFileName(fullPath);
        // Sticky toast: click it to delete the damaged file.
        Toasts.ShowError(
            string.Format(Localized.ToastCorruptFileSkipped, fileName),
            durationMs: 0,
            onClick: () => _ = PromptDeleteCorruptFileAsync(fullPath));
    }

    private async Task PromptDeleteCorruptFileAsync(string fullPath)
    {
        var fileName = System.IO.Path.GetFileName(fullPath);
        if (ConfirmActionFunc == null) return;

        var confirmed = await ConfirmActionFunc(Localized.ConfirmTitle, string.Format(Localized.ConfirmDeleteCorruptFile, fileName));
        if (!confirmed) return;

        try
        {
            _configService.DeleteFile(fullPath);
            _shownCorruptFileToasts.Remove(fullPath);
            Toasts.ShowNeutral(string.Format(Localized.ToastCorruptFileDeleted, fileName));
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to delete corrupt file '{fileName}': {ex.Message}");
            Toasts.ShowError(string.Format(Localized.ToastCorruptFileDeleteFailed, fileName));
        }
    }

    private async Task<string?> MigrateDataAsync(string sourceDir, string targetDir)
    {
        try
        {
            if (!Directory.Exists(sourceDir))
                return null;

            Directory.CreateDirectory(targetDir);

            var profilesSource = Path.Combine(sourceDir, "profiles");
            if (Directory.Exists(profilesSource))
            {
                var profilesTarget = Path.Combine(targetDir, "profiles");
                await CopyDirectoryAsync(profilesSource, profilesTarget);
            }

            var scenariosSource = Path.Combine(sourceDir, "scenarios");
            if (Directory.Exists(scenariosSource))
            {
                var scenariosTarget = Path.Combine(targetDir, "scenarios");
                await CopyDirectoryAsync(scenariosSource, scenariosTarget);
            }

            foreach (var file in new[] { "app.json", "app.log" })
            {
                var src = Path.Combine(sourceDir, file);
                if (File.Exists(src))
                {
                    var dst = Path.Combine(targetDir, file);
                    File.Copy(src, dst, overwrite: true);
                }
            }

            var llamaSource = Path.Combine(sourceDir, "llama.cpp");
            if (Directory.Exists(llamaSource))
            {
                var llamaTarget = Path.Combine(targetDir, "llama.cpp");
                await CopyDirectoryAsync(llamaSource, llamaTarget);
            }

            DeleteDirectoryContents(sourceDir);

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static async Task CopyDirectoryAsync(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            using var srcStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, true);
            using var dstStream = new FileStream(Path.Combine(targetDir, fileName), FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await srcStream.CopyToAsync(dstStream);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(dir);
            await CopyDirectoryAsync(dir, Path.Combine(targetDir, dirName));
        }
    }

    private static void DeleteDirectoryContents(string path)
    {
        if (!Directory.Exists(path)) return;

        foreach (var file in Directory.GetFiles(path))
        {
            try { File.Delete(file); } catch { }
        }

        foreach (var dir in Directory.GetDirectories(path))
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    public AppSettings GetAppSettings()
    {
        return new AppSettings
        {
            WindowWidth = _windowWidth,
            WindowHeight = _windowHeight,
            WindowLeft = _windowLeft,
            WindowTop = _windowTop,
            Language = SelectedLanguage,
            ProfileNameInput = ProfileNameInput,
            ExecutablePath = NormalizeExecutablePathForSaving(ExecutablePath),
            ModelPath = ModelPath,
            ModelsDir = ModelsDir,
            LlamaCppCustomDownloadPath = LlamaCppCustomDownloadPath,
            Host = Host,
            Port = ParseNullableInt(Port) ?? 8080,
            ContextSize = ContextSize,
            Threads = Threads,
            GpuLayers = GpuLayers,
            CpuMoe = CpuMoe,
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
            ParallelSlots = ParallelSlots,
            ContBatching = ContBatching,
            Timeout = Timeout,
            CachePrompt = CachePrompt,
            Mlock = Mlock,
            Mmap = Mmap,
            Reasoning = Reasoning,
            ReasoningBudget = ReasoningBudget,
            Seed = Seed,
            PresencePenalty = PresencePenalty,
            FrequencyPenalty = FrequencyPenalty,
            ContextShift = ContextShift,
            SpecType = SpecType,
            SpecDraftModel = SpecDraftModel,
            SpecDraftGpuLayers = SpecDraftGpuLayers,
            SpecDraftNMax = SpecDraftNMax,
            SpecDraftNMin = SpecDraftNMin,
            SpecDraftPSplit = SpecDraftPSplit,
            SpecDraftPMin = SpecDraftPMin,
            HfRepo = HfRepo,
            HfFile = HfFile,
            Offline = Offline,
            HfRepoDraft = HfRepoDraft,
            AutoRestart = AutoRestart,
            ConfirmStopServer = ConfirmStopServer,
            AutoScrollLog = AutoScroll,
            LogEnabled = LogEnabled,
            LogVisible = LogVisible,
            TabPanelVisible = TabPanelVisible,
            IsNavPaneOpen = IsNavPaneOpen,
            AutoFitHeight = AutoFitHeight,
            AutoFitHeightSavedHeight = AutoFitHeightSavedHeight,
            LogHeight = LogHeight,
            FontSizeLevel = FontSizeLevel,
            ThemeVariant = ThemeVariant,
            ColorScheme = ColorScheme,
            CustomColors = new Dictionary<string, string>(_customColors),
            FontFamily = SelectedFontFamily ?? "",
            CustomArgumentToggleStates = GetToggleStates(),
            LlamaCppInstalledTag = _llamaCppInstalledTag,
            SelectedTabIndex = SelectedTabIndex,
            RecentValuesHistory = new Dictionary<string, List<string>>(_recentValues),
            ReleaseBodyCache = new Dictionary<string, string>(_releaseBodyCache),
            ReleaseBodyCacheOrder = new List<string>(_releaseBodyCacheOrder),
            MaxLogFiles = _logService.MaxLogFiles,
            MaxLogSizeBytes = _logService.MaxLogSizeBytes,
            RunInDocker = RunInDocker,
            DockerImage = DockerImage,
            DockerGpuAll = DockerGpuAll,
            DockerRm = DockerRm,
            DockerContainerName = DockerContainerName,
            LastAppUpdateCheck = _lastAppUpdateCheck,
            LastLlamaUpdateCheck = _lastLlamaUpdateCheck,
            AppUpdateCheckIntervalMinutes = _appUpdateCheckInterval,
            LlamaUpdateCheckIntervalMinutes = _llamaUpdateCheckInterval,
            CachedLlamaReleasesJson = System.Text.Json.JsonSerializer.Serialize(_cachedLlamaReleases),
            CachedLlamaReleasesTimestamp = _cachedLlamaReleasesTimestamp,
            LogStreamEnabled = _logStreamEnabled,
            LogStreamPort = _logStreamPort,
            LogStreamToken = _logStreamToken,
            OnDemandProxyEnabled = _onDemandProxyEnabled,
            OnDemandProxyPort = _onDemandProxyPort,
            OnDemandProxyIdleSeconds = _onDemandProxyIdleSeconds,
            OnDemandProxyHealthTimeoutSeconds = _onDemandProxyHealthTimeoutSeconds,
            OnDemandProxyApiKey = _onDemandProxyApiKey,
            OnDemandProxyMiniWindowEnabled = _onDemandProxyMiniWindowEnabled,
            ComfyUiFreeEnabled = _comfyUiFreeEnabled,
            ComfyUiUrl = _comfyUiUrl,
            ScenariosEnabled = _scenariosEnabled,
            SelectedScenario = _selectedScenario,
            AutoStartWithSystem = _autoStartWithSystem,
            CustomBrowserPath = _customBrowserPath,
            DialogGeometry = new Dictionary<string, DialogGeometry>(DialogGeometryDict),
            ExperimentalReposEnabled = _experimentalReposEnabled,
            ExperimentalUpdateCheckIntervalMinutes = _experimentalUpdateCheckInterval,
            LastExperimentalUpdateCheck = _lastExperimentalUpdateCheck,
            ExperimentalRepos = ExperimentalRepos.ToList()
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
                if (StartServerCommand is AsyncRelayCommand startCmd)
                    startCmd.RaiseCanExecuteChanged();
                _ = RefreshSupportedFlagsAsync();
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
                UpdateModelLoadModeStatus();
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
                UpdateModelLoadModeStatus();
                if (StartServerCommand is AsyncRelayCommand startCmd)
                    startCmd.RaiseCanExecuteChanged();
            }
        }
    }

    public string LlamaCppCustomDownloadPath
    {
        get => _llamaCppCustomDownloadPath;
        set
        {
            if (_llamaCppCustomDownloadPath != value)
            {
                _llamaCppCustomDownloadPath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasUnsavedChanges));
            }
        }
    }

    public string Host
    {
        get => _host;
        set
        {
            _host = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasUnsavedChanges));
            OnPropertyChanged(nameof(IsHostValid));
            OnPropertyChanged(nameof(HostValidationMessage));
            UpdateCurrentCommand();
            UpdateCommandStates();
        }
    }

    public bool IsHostValid
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_host)) return true; // empty -> default 127.0.0.1
            return IsValidHost(_host.Trim());
        }
    }

    public string HostValidationMessage =>
        IsHostValid ? string.Empty : LocalizedStrings.Instance.HostValidationWarning;

    private static bool IsValidHost(string host)
    {
        if (System.Net.IPAddress.TryParse(host, out _)) return true;
        if (Uri.CheckHostName(host) != UriHostNameType.Dns) return false;
        // A DNS name whose first label is all digits is almost always a mistyped IPv4
        // address (e.g. "127.0.0.1dfgvbd" or "999.1.1.1"); require a real IP in that case.
        var firstLabel = host.Split('.')[0];
        return firstLabel.Length > 0 && !firstLabel.All(char.IsDigit);
    }

    public string Port
    {
        get => _port;
        set
        {
            _port = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasUnsavedChanges));
            OnPropertyChanged(nameof(PortValidationMessage));
            OnPropertyChanged(nameof(IsPortValid));
            UpdateCurrentCommand();
            UpdateCommandStates();
        }
    }

    public bool IsPortValid
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_port)) return true; // empty is ok, will use default
            return int.TryParse(_port, out var p) && p >= 1 && p <= 65535;
        }
    }

    public string PortValidationMessage =>
        string.IsNullOrWhiteSpace(_port) || IsPortValid
            ? string.Empty
            : LocalizedStrings.Instance.PortValidationWarning;

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

    public string CpuMoe
    {
        get => _cpuMoe;
        set { _cpuMoe = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
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
        set
        {
            if (_suppressCacheTypeKChange) return;
            _cacheTypeK = value;

            if (!string.IsNullOrEmpty(value) && !_cacheTypeOptions.Contains(value))
            {
                _cacheTypeOptions = new List<string>(_cacheTypeOptions) { value };
                _suppressCacheTypeKChange = true;
                _suppressCacheTypeVChange = true;
                try
                {
                    OnPropertyChanged(nameof(CacheTypeOptions));
                }
                finally
                {
                    _suppressCacheTypeKChange = false;
                    _suppressCacheTypeVChange = false;
                }
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasUnsavedChanges));
            OnPropertyChanged(nameof(HasUnsupportedCacheTypeK));
            OnPropertyChanged(nameof(CacheTypeKBorderBrush));
            OnPropertyChanged(nameof(CacheTypeKToolTip));
            UpdateCurrentCommand();
        }
    }

    public string CacheTypeV
    {
        get => _cacheTypeV;
        set
        {
            if (_suppressCacheTypeVChange) return;
            _cacheTypeV = value;

            if (!string.IsNullOrEmpty(value) && !_cacheTypeOptions.Contains(value))
            {
                _cacheTypeOptions = new List<string>(_cacheTypeOptions) { value };
                _suppressCacheTypeKChange = true;
                _suppressCacheTypeVChange = true;
                try
                {
                    OnPropertyChanged(nameof(CacheTypeOptions));
                }
                finally
                {
                    _suppressCacheTypeKChange = false;
                    _suppressCacheTypeVChange = false;
                }
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasUnsavedChanges));
            OnPropertyChanged(nameof(HasUnsupportedCacheTypeV));
            OnPropertyChanged(nameof(CacheTypeVBorderBrush));
            OnPropertyChanged(nameof(CacheTypeVToolTip));
            UpdateCurrentCommand();
        }
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
        set
        {
            _enableWebUI = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasUnsavedChanges));
            OnPropertyChanged(nameof(CanOpenInBrowser));
            UpdateCurrentCommand();
            if (OpenInBrowserCommand is AsyncRelayCommand openCmd)
                openCmd.RaiseCanExecuteChanged();
        }
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

    public string ParallelSlots
    {
        get => _parallelSlots;
        set { _parallelSlots = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public bool? ContBatching
    {
        get => _contBatching;
        set { _contBatching = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public string Timeout
    {
        get => _timeout;
        set { _timeout = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public bool? CachePrompt
    {
        get => _cachePrompt;
        set { _cachePrompt = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public bool? Mlock
    {
        get => _mlock;
        set { _mlock = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public bool? Mmap
    {
        get => _mmap;
        set { _mmap = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public bool? Reasoning
    {
        get => _reasoning;
        set { _reasoning = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public string ReasoningBudget
    {
        get => _reasoningBudget;
        set { _reasoningBudget = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public string Seed
    {
        get => _seed;
        set { _seed = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public string PresencePenalty
    {
        get => _presencePenalty;
        set { _presencePenalty = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public string FrequencyPenalty
    {
        get => _frequencyPenalty;
        set { _frequencyPenalty = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public bool? ContextShift
    {
        get => _contextShift;
        set { _contextShift = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public string SpecType
    {
        get => _specType;
        set
        {
            if (_suppressSpecTypeChange) return;
            _specType = value;

            if (!string.IsNullOrEmpty(value) && !_specTypeOptions.Contains(value))
            {
                _specTypeOptions = new List<string>(_specTypeOptions) { value };
                _suppressSpecTypeChange = true;
                try
                {
                    OnPropertyChanged(nameof(SpecTypeOptions));
                }
                finally
                {
                    _suppressSpecTypeChange = false;
                }
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasUnsavedChanges));
            OnPropertyChanged(nameof(ShowSpecFields));
            OnPropertyChanged(nameof(ShowDraftModelFields));
            OnPropertyChanged(nameof(HasUnsupportedSpecType));
            OnPropertyChanged(nameof(SpecTypeBorderBrush));
            OnPropertyChanged(nameof(SpecTypeToolTip));
            UpdateCurrentCommand();
        }
    }

    public string SpecDraftModel
    {
        get => _specDraftModel;
        set { _specDraftModel = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public string SpecDraftGpuLayers
    {
        get => _specDraftGpuLayers;
        set { _specDraftGpuLayers = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public string SpecDraftNMax
    {
        get => _specDraftNMax;
        set { _specDraftNMax = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public string SpecDraftNMin
    {
        get => _specDraftNMin;
        set { _specDraftNMin = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public string SpecDraftPSplit
    {
        get => _specDraftPSplit;
        set { _specDraftPSplit = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public string SpecDraftPMin
    {
        get => _specDraftPMin;
        set { _specDraftPMin = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); UpdateCurrentCommand(); }
    }

    public string HfRepo
    {
        get => _hfRepo;
        set
        {
            if (_hfRepo != value)
            {
                _hfRepo = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanStartServer));
                OnPropertyChanged(nameof(HasUnsavedChanges));
                OnPropertyChanged(nameof(HasUnsupportedHfRepo));
                OnPropertyChanged(nameof(HfRepoBorderBrush));
                OnPropertyChanged(nameof(HfRepoToolTip));
                UpdateCurrentCommand();
                UpdateModelLoadModeStatus();
                if (StartServerCommand is AsyncRelayCommand startCmd)
                    startCmd.RaiseCanExecuteChanged();
            }
        }
    }

    public string HfFile
    {
        get => _hfFile;
        set
        {
            if (_hfFile != value)
            {
                _hfFile = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanStartServer));
                OnPropertyChanged(nameof(HasUnsavedChanges));
                OnPropertyChanged(nameof(HasUnsupportedHfFile));
                OnPropertyChanged(nameof(HfFileBorderBrush));
                OnPropertyChanged(nameof(HfFileToolTip));
                UpdateCurrentCommand();
                UpdateModelLoadModeStatus();
                if (StartServerCommand is AsyncRelayCommand startCmd)
                    startCmd.RaiseCanExecuteChanged();
            }
        }
    }

    public bool Offline
    {
        get => _offline;
        set
        {
            if (_offline != value)
            {
                _offline = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasUnsavedChanges));
                OnPropertyChanged(nameof(HasUnsupportedOffline));
                OnPropertyChanged(nameof(OfflineToolTip));
                UpdateCurrentCommand();
            }
        }
    }

    public string HfRepoDraft
    {
        get => _hfRepoDraft;
        set
        {
            if (_hfRepoDraft != value)
            {
                _hfRepoDraft = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasUnsavedChanges));
                OnPropertyChanged(nameof(HasUnsupportedHfRepoDraft));
                OnPropertyChanged(nameof(HfRepoDraftBorderBrush));
                OnPropertyChanged(nameof(HfRepoDraftToolTip));
                UpdateCurrentCommand();
            }
        }
    }

    public bool RunInDocker
    {
        get => _runInDocker;
        set
        {
            if (_runInDocker != value)
            {
                _runInDocker = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasUnsavedChanges));
                OnPropertyChanged(nameof(CanStartServer));
                UpdateCurrentCommand();
                if (StartServerCommand is AsyncRelayCommand startCmd)
                    startCmd.RaiseCanExecuteChanged();
            }
        }
    }

    public string DockerImage
    {
        get => _dockerImage;
        set
        {
            if (_dockerImage != value)
            {
                _dockerImage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasUnsavedChanges));
                UpdateCurrentCommand();
            }
        }
    }

    public bool DockerGpuAll
    {
        get => _dockerGpuAll;
        set
        {
            if (_dockerGpuAll != value)
            {
                _dockerGpuAll = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasUnsavedChanges));
                UpdateCurrentCommand();
            }
        }
    }

    public bool DockerRm
    {
        get => _dockerRm;
        set
        {
            if (_dockerRm != value)
            {
                _dockerRm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasUnsavedChanges));
                UpdateCurrentCommand();
            }
        }
    }

    public string DockerContainerName
    {
        get => _dockerContainerName;
        set
        {
            if (_dockerContainerName != value)
            {
                _dockerContainerName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasUnsavedChanges));
                UpdateCurrentCommand();
            }
        }
    }

    public bool IsDockerAvailable
    {
        get => _isDockerAvailable;
        set
        {
            if (_isDockerAvailable != value)
            {
                _isDockerAvailable = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DockerTabEnabled));
                OnPropertyChanged(nameof(DockerNotInstalledWarning));
            }
        }
    }

    public bool DockerTabEnabled => _isDockerAvailable;
    public bool DockerNotInstalledWarning => !_isDockerAvailable;

    public bool ShowSpecFields => !string.IsNullOrEmpty(SpecType) && SpecType != "none";

    public bool ShowDraftModelFields => ShowSpecFields && SpecType == "draft-simple";

    public List<string> SpecTypeOptions => _specTypeOptions;

    public bool AutoRestart
    {
        get => _autoRestart;
        set
        {
            _autoRestart = value;
            if (_selectedInstance != null && !_isLoadingConfig)
                _selectedInstance.AutoRestart = value;
            OnPropertyChanged();
        }
    }

    public bool ConfirmStopServer
    {
        get => _confirmStopServer;
        set { _confirmStopServer = value; OnPropertyChanged(); }
    }

    public bool AutoStartWithSystem
    {
        get => _autoStartWithSystem;
        set
        {
            if (_autoStartWithSystem != value)
            {
                _autoStartWithSystem = value;
                _autoStartService.SetAutoStart(value);
                OnPropertyChanged();
                _ = SaveSettingsAsync();
            }
        }
    }

    public string CustomBrowserPath
    {
        get => _customBrowserPath;
        set
        {
            if (_customBrowserPath != value)
            {
                _customBrowserPath = value ?? "";
                ServerInstance.CustomBrowserPath = string.IsNullOrWhiteSpace(_customBrowserPath) ? null : _customBrowserPath;
                OnPropertyChanged();
                _ = SaveSettingsAsync();
            }
        }
    }

    public ObservableCollection<Models.BrowserInfo> DetectedBrowsers { get; } = new();

    public bool HasDetectedBrowsers => DetectedBrowsers.Count > 0;

    private Models.BrowserInfo? _selectedBrowser;
    public Models.BrowserInfo? SelectedBrowser
    {
        get => _selectedBrowser;
        set
        {
            if (_selectedBrowser == value) return;
            _selectedBrowser = value;
            OnPropertyChanged();
            if (value != null && !string.IsNullOrWhiteSpace(value.Path))
                CustomBrowserPath = value.Path;
        }
    }

    public async Task LoadDetectedBrowsersAsync()
    {
        try
        {
            var browsers = await Task.Run(BrowserDetectionService.DetectInstalledBrowsers);
            DetectedBrowsers.Clear();
            foreach (var b in browsers)
                DetectedBrowsers.Add(b);

            // Pre-select the entry matching the saved path (set the field directly so we
            // don't overwrite a custom path the user typed manually).
            _selectedBrowser = DetectedBrowsers.FirstOrDefault(
                b => string.Equals(b.Path, _customBrowserPath, StringComparison.OrdinalIgnoreCase));
            OnPropertyChanged(nameof(SelectedBrowser));
            OnPropertyChanged(nameof(HasDetectedBrowsers));
        }
        catch
        {
            // Detection failures are non-fatal; the manual path field still works.
        }
    }

    public bool AutoScroll
    {
        get => _autoScroll;
        set { _autoScroll = value; OnPropertyChanged(); }
    }

    public bool LogEnabled
    {
        get => _logEnabled;
        set
        {
            _logEnabled = value;
            if (_selectedInstance != null && !_isLoadingConfig)
                _selectedInstance.LogEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ToggleLogButtonText));
        }
    }

    public bool LogVisible
    {
        get => _logVisible;
        set
        {
            // Hiding the log also drops the maximized layout.
            if (!value && _isLogMaximized)
                IsLogMaximized = false;
            _logVisible = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ToggleLogButtonText));
            OnPropertyChanged(nameof(IsLogSplitterVisible));
        }
    }

    private bool _isLogMaximized;
    /// <summary>
    /// Hides the settings/tabs panel so the log can expand to fill the freed space;
    /// the server control panel moves up directly above the log.
    /// </summary>
    public bool IsLogMaximized
    {
        get => _isLogMaximized;
        set
        {
            if (_isLogMaximized != value)
            {
                _isLogMaximized = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsLogSplitterVisible));
            }
        }
    }

    // Splitter is only useful while the log is shown and not maximized.
    public bool IsLogSplitterVisible => LogVisible && !IsLogMaximized;

    public bool TabPanelVisible
    {
        get => _tabPanelVisible;
        set { _tabPanelVisible = value; OnPropertyChanged(); OnPropertyChanged(nameof(ToggleTabPanelButtonText)); }
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set { _selectedTabIndex = value; OnPropertyChanged(); }
    }

    public bool IsNavPaneOpen
    {
        get => _isNavPaneOpen;
        set { _isNavPaneOpen = value; OnPropertyChanged(); }
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

    public double WindowWidth
    {
        get => _windowWidth;
        set { _windowWidth = value; }
    }

    public double WindowHeight
    {
        get => _windowHeight;
        set { _windowHeight = value; }
    }

    public double? WindowLeft
    {
        get => _windowLeft;
        set { _windowLeft = value; }
    }

    public double? WindowTop
    {
        get => _windowTop;
        set { _windowTop = value; }
    }

    private double _logHeight = 200;
    public double LogHeight
    {
        get => _logHeight;
        set { _logHeight = value; OnPropertyChanged(); }
    }

    private bool _isLlamaUpdateAvailable;
    public bool IsLlamaUpdateAvailable
    {
        get => _isLlamaUpdateAvailable;
        set
        {
            if (_isLlamaUpdateAvailable != value)
            {
                _isLlamaUpdateAvailable = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowLlamaUpdateButton));
                OnPropertyChanged(nameof(ShowLlamaDownloadButton));
                OnPropertyChanged(nameof(ShowLlamaChangeVersionButton));
                OnPropertyChanged(nameof(LlamaButtonText));
            }
        }
    }

    private string _llamaUpdateTooltip = "";
    public string LlamaUpdateTooltip
    {
        get => _llamaUpdateTooltip;
        set { _llamaUpdateTooltip = value; OnPropertyChanged(); }
    }

    public bool ShowLlamaUpdateButton => _downloadService.IsLlamaCppInstalled() && _isLlamaUpdateAvailable;
    public bool ShowLlamaDownloadButton => !_downloadService.IsLlamaCppInstalled();
    public bool ShowLlamaChangeVersionButton => _downloadService.IsLlamaCppInstalled() && !_isLlamaUpdateAvailable;
    public string LlamaButtonText => _downloadService.IsLlamaCppInstalled()
        ? LocalizedStrings.Instance.UpdateLlama
        : LocalizedStrings.Instance.DownloadLlama;

    public string LlamaInstalledVersionTooltip
    {
        get
        {
            if (string.IsNullOrEmpty(_llamaCppInstalledTag)) return "";
            return string.Format(LocalizedStrings.GetString("InstalledVersionTooltip"), _llamaCppInstalledTag);
        }
    }

    private bool _isAppUpdateAvailable;
    private string _appUpdateTooltip = "";
    private AppUpdateInfo? _pendingAppUpdate;

    public bool ShowAppUpdateButton => _isAppUpdateAvailable;

    public string? AvailableAppUpdateTag => _isAppUpdateAvailable ? _pendingAppUpdate?.Tag : null;
    public string AppUpdateTooltip
    {
        get => _appUpdateTooltip;
        set { _appUpdateTooltip = value; OnPropertyChanged(); }
    }

    public string ExecutablePathPlaceholder => LocalizedStrings.Instance.ExecutablePathPlaceholder;
    public string ModelPathPlaceholder => LocalizedStrings.Instance.PlaceholderModelPath;
    public string ModelsDirPlaceholder => LocalizedStrings.Instance.PlaceholderModelsDir;
    public string HostPlaceholder => LocalizedStrings.Instance.PlaceholderHost;
    public string PortPlaceholder => LocalizedStrings.Instance.PlaceholderPort;
    public string ContextSizePlaceholder => LocalizedStrings.Instance.PlaceholderContextSize;
    public string ThreadsPlaceholder => LocalizedStrings.Instance.PlaceholderThreads;
    public string GpuLayersPlaceholder => LocalizedStrings.Instance.PlaceholderGpuLayers;
    public string CpuMoePlaceholder => LocalizedStrings.Instance.PlaceholderCpuMoe;
    public string TemperaturePlaceholder => LocalizedStrings.Instance.PlaceholderTemperature;
    public string MaxTokensPlaceholder => LocalizedStrings.Instance.PlaceholderMaxTokens;
    public string BatchSizePlaceholder => LocalizedStrings.Instance.PlaceholderBatchSize;
    public string UBatchSizePlaceholder => LocalizedStrings.Instance.PlaceholderUBatchSize;
    public string MinPPlaceholder => LocalizedStrings.Instance.PlaceholderMinP;
    public string TopKPlaceholder => LocalizedStrings.Instance.PlaceholderTopK;
    public string TopPPlaceholder => LocalizedStrings.Instance.PlaceholderTopP;
    public string RepeatPenaltyPlaceholder => LocalizedStrings.Instance.PlaceholderRepeatPenalty;
    public string ApiKeyPlaceholder => LocalizedStrings.Instance.PlaceholderApiKey;
    public string AliasPlaceholder => LocalizedStrings.Instance.PlaceholderAlias;
    public string LogFilePathPlaceholder => LocalizedStrings.Instance.PlaceholderLogFilePath;
    public string MmprojPathPlaceholder => LocalizedStrings.Instance.PlaceholderMmprojPath;
    public string CustomArgumentsPlaceholder => LocalizedStrings.Instance.PlaceholderCustomArguments;

    private bool IsPropertySupported(params string[] flags)
    {
        if (_supportedFlags == null) return true;
        return flags.Any(f => _supportedFlags.Contains(f));
    }

    public bool IsParallelSlotsSupported => IsPropertySupported("--parallel", "-np");
    public bool IsContBatchingSupported => IsPropertySupported("--cont-batching", "-cb");
    public bool IsTimeoutSupported => IsPropertySupported("--timeout", "-to");
    public bool IsCachePromptSupported => IsPropertySupported("--cache-prompt");
    public bool IsMlockSupported => IsPropertySupported("--mlock");
    public bool IsMmapSupported => IsPropertySupported("--mmap");
    public bool IsReasoningSupported => IsPropertySupported("--reasoning", "-rea");
    public bool IsReasoningBudgetSupported => IsPropertySupported("--reasoning-budget");
    public bool IsSeedSupported => IsPropertySupported("--seed", "-s");
    public bool IsPresencePenaltySupported => IsPropertySupported("--presence-penalty");
    public bool IsFrequencyPenaltySupported => IsPropertySupported("--frequency-penalty");
    public bool IsContextShiftSupported => IsPropertySupported("--context-shift");
    public bool IsSpecTypeSupported => IsPropertySupported("--spec-type");
    public bool IsCacheTypeKSupported => IsPropertySupported("-ctk", "--cache-type-k");
    public bool IsCacheTypeVSupported => IsPropertySupported("-ctv", "--cache-type-v");
    public bool IsDraftModelSupported => IsPropertySupported("-md", "--spec-draft-model", "--model-draft");
    public bool IsDraftGpuLayersSupported => IsPropertySupported("-ngld", "--spec-draft-ngl", "--gpu-layers-draft", "--n-gpu-layers-draft");
    public bool IsSpecDraftNMaxSupported => IsPropertySupported("--spec-draft-n-max");
    public bool IsSpecDraftNMinSupported => IsPropertySupported("--spec-draft-n-min");
    public bool IsSpecDraftPSplitSupported => IsPropertySupported("--spec-draft-p-split", "--draft-p-split");
    public bool IsSpecDraftPMinSupported => IsPropertySupported("--spec-draft-p-min", "--draft-p-min");
    public bool IsHfRepoSupported => IsPropertySupported("--hf-repo", "-hf", "-hfr");
    public bool IsHfFileSupported => IsPropertySupported("--hf-file", "-hff");
    public bool IsOfflineSupported => IsPropertySupported("--offline");
    public bool IsHfRepoDraftSupported => IsPropertySupported("--hf-repo-draft", "-hfd", "-hfrd");
    public bool IsHelpAvailable => _supportedFlags != null;

    public string ParallelSlotsPlaceholder => LocalizedStrings.Instance.PlaceholderParallelSlots;
    public string TimeoutPlaceholder => LocalizedStrings.Instance.PlaceholderTimeout;
    public string ReasoningBudgetPlaceholder => LocalizedStrings.Instance.PlaceholderReasoningBudget;
    public string SeedPlaceholder => LocalizedStrings.Instance.PlaceholderSeed;
    public string PresencePenaltyPlaceholder => LocalizedStrings.Instance.PlaceholderPresencePenalty;
    public string FrequencyPenaltyPlaceholder => LocalizedStrings.Instance.PlaceholderFrequencyPenalty;
    public string SpecDraftNMaxPlaceholder => LocalizedStrings.Instance.PlaceholderSpecDraftNMax;
    public string SpecDraftNMinPlaceholder => LocalizedStrings.Instance.PlaceholderSpecDraftNMin;
    public string SpecDraftPSplitPlaceholder => LocalizedStrings.Instance.PlaceholderSpecDraftPSplit;
    public string SpecDraftPMinPlaceholder => LocalizedStrings.Instance.PlaceholderSpecDraftPMin;
    public string SpecDraftGpuLayersPlaceholder => LocalizedStrings.Instance.PlaceholderSpecDraftGpuLayers;
    public string SpecDraftModelPlaceholder => LocalizedStrings.Instance.PlaceholderSpecDraftModel;
    public string HfRepoPlaceholder => LocalizedStrings.Instance.PlaceholderHfRepo;
    public string HfFilePlaceholder => LocalizedStrings.Instance.PlaceholderHfFile;
    public string HfRepoDraftPlaceholder => LocalizedStrings.Instance.PlaceholderHfRepoDraft;
    public string FeatureNotSupportedTooltip => LocalizedStrings.Instance.FeatureNotSupported;
    public string UnsupportedArgWarningText => LocalizedStrings.Instance.UnsupportedArgWarning;
    public string ToastClickToApplyText => LocalizedStrings.Instance.ToastClickToApply;

    /// <summary>
    /// True when the SpecType control has a meaningful value that is not supported
    /// by the current executable (either --spec-type flag is missing, or the value
    /// is not in the valid values list parsed from help).
    /// Used to show a visual warning indicator.
    /// </summary>
    public bool HasUnsupportedSpecType => !string.IsNullOrEmpty(SpecType)
        && SpecType != "none"
        && (!IsSpecTypeSupported || !_validSpecTypeValues.Contains(SpecType));

    private static readonly Avalonia.Media.IBrush _warningBrush =
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(255, 165, 0)); // Orange
    private static readonly Avalonia.Media.IBrush _transparentBrush =
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(0, 0, 0, 0));

    private static readonly Avalonia.Media.IBrush _accentBrush =
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0, 150, 136));

    public bool ShowModelLoadModeStatus =>
        !string.IsNullOrEmpty(ModelPath) || !string.IsNullOrEmpty(ModelsDir)
        || !string.IsNullOrEmpty(HfRepo) || !string.IsNullOrEmpty(HfFile);

    public string ModelLoadModeStatus
    {
        get
        {
            if (!string.IsNullOrEmpty(ModelPath))
            {
                var fileName = Path.GetFileName(ModelPath);
                return string.Format(LocalizedStrings.Instance.ModelLoadModeStatusSingleModel, fileName);
            }
            if (!string.IsNullOrEmpty(ModelsDir))
                return LocalizedStrings.Instance.ModelLoadModeStatusRouting;
            if (!string.IsNullOrEmpty(HfRepo) || !string.IsNullOrEmpty(HfFile))
                return LocalizedStrings.Instance.ModelLoadModeStatusHfRepo;
            return string.Empty;
        }
    }

    public Avalonia.Media.IBrush ModelLoadModeStatusBrush =>
        (!string.IsNullOrEmpty(ModelPath) && !string.IsNullOrEmpty(ModelsDir))
        || (!string.IsNullOrEmpty(ModelPath) && (!string.IsNullOrEmpty(HfRepo) || !string.IsNullOrEmpty(HfFile)))
            ? _warningBrush
            : _accentBrush;

    private void UpdateModelLoadModeStatus()
    {
        OnPropertyChanged(nameof(ShowModelLoadModeStatus));
        OnPropertyChanged(nameof(ModelLoadModeStatus));
        OnPropertyChanged(nameof(ModelLoadModeStatusBrush));

        if (_isLoadingConfig)
            return;

        bool hasModelPathConflict = !string.IsNullOrEmpty(ModelPath) && !string.IsNullOrEmpty(ModelsDir);
        bool hasHfConflict = !string.IsNullOrEmpty(ModelPath) && (!string.IsNullOrEmpty(HfRepo) || !string.IsNullOrEmpty(HfFile));

        if (hasModelPathConflict)
        {
            var conflictKey = "modelsdir";
            if (_shownConflictToasts.Add(conflictKey))
            {
                var fileName = Path.GetFileName(ModelPath);
                var toastText = string.Format(LocalizedStrings.Instance.ModelLoadModeConflictToast, fileName);
                Toasts.Show(toastText);
            }
        }
        else if (hasHfConflict)
        {
            var conflictKey = "hfrepo";
            if (_shownConflictToasts.Add(conflictKey))
            {
                var fileName = Path.GetFileName(ModelPath);
                var toastText = string.Format(LocalizedStrings.Instance.ModelLoadModeConflictHfToast, fileName);
                Toasts.Show(toastText);
            }
        }
        else
        {
            _shownConflictToasts.Clear();
        }
    }

    /// <summary>
    /// Warns when auto-fit is off and the active tab's content overflows its viewport.
    /// Called from MainWindow after layout.
    /// </summary>
    public void CheckAutoFitHeightWarning(bool tabContentClipped)
    {
        if (AutoFitHeight || !TabPanelVisible)
            return;

        // Only warn on real overflow; a tab that fits without scrolling shouldn't nag.
        if (!tabContentClipped)
        {
            // Content now fits, so drop any stale warning.
            if (_autoFitHeightToast != null)
            {
                Toasts.Dismiss(_autoFitHeightToast);
                _autoFitHeightToast = null;
            }
            return;
        }

        // Only show the warning once per session.
        if (_autoFitHeightWarningShown)
            return;

        // Dismiss existing toast if still showing
        if (_autoFitHeightToast != null)
        {
            Toasts.Dismiss(_autoFitHeightToast);
            _autoFitHeightToast = null;
        }

        _autoFitHeightWarningShown = true;

        var msg = LocalizedStrings.Instance.ToastAutoFitHeightDisabled;
        _autoFitHeightToast = new ToastItem(msg, onClick: () =>
        {
            AutoFitHeight = true;
            _autoFitHeightToast = null;
        });

        Toasts.Toasts.Add(_autoFitHeightToast);

        // Auto-dismiss after 6 seconds
        var capturedToast = _autoFitHeightToast;
        _ = System.Threading.Tasks.Task.Delay(6000).ContinueWith(_ =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_autoFitHeightToast == capturedToast)
                    _autoFitHeightToast = null;
                Toasts.Toasts.Remove(capturedToast);
            });
        });
    }

    public Avalonia.Media.IBrush SpecTypeBorderBrush => HasUnsupportedSpecType ? _warningBrush : _transparentBrush;

    public string SpecTypeToolTip => HasUnsupportedSpecType
        ? UnsupportedArgWarningText
        : (LocalizedStrings.Instance.TooltipSpecType ?? "");

    /// <summary>
    /// True when CacheTypeK has a value not supported by the current executable.
    /// </summary>
    public bool HasUnsupportedCacheTypeK => !string.IsNullOrEmpty(CacheTypeK)
        && (!IsCacheTypeKSupported || (_validCacheTypeValues.Count > 0 && !_validCacheTypeValues.Contains(CacheTypeK)));

    /// <summary>
    /// True when CacheTypeV has a value not supported by the current executable.
    /// </summary>
    public bool HasUnsupportedCacheTypeV => !string.IsNullOrEmpty(CacheTypeV)
        && (!IsCacheTypeVSupported || (_validCacheTypeValues.Count > 0 && !_validCacheTypeValues.Contains(CacheTypeV)));

    public Avalonia.Media.IBrush CacheTypeKBorderBrush => HasUnsupportedCacheTypeK ? _warningBrush : _transparentBrush;
    public Avalonia.Media.IBrush CacheTypeVBorderBrush => HasUnsupportedCacheTypeV ? _warningBrush : _transparentBrush;

    public string CacheTypeKToolTip => HasUnsupportedCacheTypeK
        ? UnsupportedArgWarningText
        : (LocalizedStrings.Instance.TooltipCacheTypeK ?? "");

    public string CacheTypeVToolTip => HasUnsupportedCacheTypeV
        ? UnsupportedArgWarningText
        : (LocalizedStrings.Instance.TooltipCacheTypeV ?? "");

    // Unsupported warning indicators for spec params
    public bool HasUnsupportedDraftModel => !IsDraftModelSupported && !string.IsNullOrEmpty(SpecDraftModel);
    public bool HasUnsupportedDraftGpuLayers => !IsDraftGpuLayersSupported && !string.IsNullOrEmpty(SpecDraftGpuLayers);
    public bool HasUnsupportedSpecDraftNMax => !IsSpecDraftNMaxSupported && !string.IsNullOrEmpty(SpecDraftNMax);
    public bool HasUnsupportedSpecDraftNMin => !IsSpecDraftNMinSupported && !string.IsNullOrEmpty(SpecDraftNMin);
    public bool HasUnsupportedSpecDraftPSplit => !IsSpecDraftPSplitSupported && !string.IsNullOrEmpty(SpecDraftPSplit);
    public bool HasUnsupportedSpecDraftPMin => !IsSpecDraftPMinSupported && !string.IsNullOrEmpty(SpecDraftPMin);

    public Avalonia.Media.IBrush DraftModelBorderBrush => HasUnsupportedDraftModel ? _warningBrush : _transparentBrush;
    public Avalonia.Media.IBrush DraftGpuLayersBorderBrush => HasUnsupportedDraftGpuLayers ? _warningBrush : _transparentBrush;
    public Avalonia.Media.IBrush SpecDraftNMaxBorderBrush => HasUnsupportedSpecDraftNMax ? _warningBrush : _transparentBrush;
    public Avalonia.Media.IBrush SpecDraftNMinBorderBrush => HasUnsupportedSpecDraftNMin ? _warningBrush : _transparentBrush;
    public Avalonia.Media.IBrush SpecDraftPSplitBorderBrush => HasUnsupportedSpecDraftPSplit ? _warningBrush : _transparentBrush;
    public Avalonia.Media.IBrush SpecDraftPMinBorderBrush => HasUnsupportedSpecDraftPMin ? _warningBrush : _transparentBrush;

    public string DraftModelToolTip => HasUnsupportedDraftModel ? UnsupportedArgWarningText : (LocalizedStrings.Instance.TooltipSpecDraftModel ?? "");
    public string DraftGpuLayersToolTip => HasUnsupportedDraftGpuLayers ? UnsupportedArgWarningText : (LocalizedStrings.Instance.TooltipSpecDraftGpuLayers ?? "");
    public string SpecDraftNMaxToolTip => HasUnsupportedSpecDraftNMax ? UnsupportedArgWarningText : (LocalizedStrings.Instance.TooltipSpecDraftNMax ?? "");
    public string SpecDraftNMinToolTip => HasUnsupportedSpecDraftNMin ? UnsupportedArgWarningText : (LocalizedStrings.Instance.TooltipSpecDraftNMin ?? "");
    public string SpecDraftPSplitToolTip => HasUnsupportedSpecDraftPSplit ? UnsupportedArgWarningText : (LocalizedStrings.Instance.TooltipSpecDraftPSplit ?? "");
    public string SpecDraftPMinToolTip => HasUnsupportedSpecDraftPMin ? UnsupportedArgWarningText : (LocalizedStrings.Instance.TooltipSpecDraftPMin ?? "");

    // Unsupported warning indicators for HF params
    public bool HasUnsupportedHfRepo => !IsHfRepoSupported && !string.IsNullOrEmpty(HfRepo);
    public bool HasUnsupportedHfFile => !IsHfFileSupported && !string.IsNullOrEmpty(HfFile);
    public bool HasUnsupportedOffline => !IsOfflineSupported && Offline;
    public bool HasUnsupportedHfRepoDraft => !IsHfRepoDraftSupported && !string.IsNullOrEmpty(HfRepoDraft);

    public Avalonia.Media.IBrush HfRepoBorderBrush => HasUnsupportedHfRepo ? _warningBrush : _transparentBrush;
    public Avalonia.Media.IBrush HfFileBorderBrush => HasUnsupportedHfFile ? _warningBrush : _transparentBrush;
    public Avalonia.Media.IBrush HfRepoDraftBorderBrush => HasUnsupportedHfRepoDraft ? _warningBrush : _transparentBrush;

    public string HfRepoToolTip => HasUnsupportedHfRepo ? UnsupportedArgWarningText : (LocalizedStrings.Instance.TooltipHfRepo ?? "");
    public string HfFileToolTip => HasUnsupportedHfFile ? UnsupportedArgWarningText : (LocalizedStrings.Instance.TooltipHfFile ?? "");
    public string OfflineToolTip => HasUnsupportedOffline ? UnsupportedArgWarningText : (LocalizedStrings.Instance.TooltipOffline ?? "");
    public string HfRepoDraftToolTip => HasUnsupportedHfRepoDraft ? UnsupportedArgWarningText : (LocalizedStrings.Instance.TooltipHfRepoDraft ?? "");

    private void UpdateFeatureSupportProperties()
    {
        OnPropertyChanged(nameof(IsParallelSlotsSupported));
        OnPropertyChanged(nameof(IsContBatchingSupported));
        OnPropertyChanged(nameof(IsTimeoutSupported));
        OnPropertyChanged(nameof(IsCachePromptSupported));
        OnPropertyChanged(nameof(IsMlockSupported));
        OnPropertyChanged(nameof(IsMmapSupported));
        OnPropertyChanged(nameof(IsReasoningSupported));
        OnPropertyChanged(nameof(IsReasoningBudgetSupported));
        OnPropertyChanged(nameof(IsSeedSupported));
        OnPropertyChanged(nameof(IsPresencePenaltySupported));
        OnPropertyChanged(nameof(IsFrequencyPenaltySupported));
        OnPropertyChanged(nameof(IsContextShiftSupported));
        OnPropertyChanged(nameof(IsSpecTypeSupported));
        OnPropertyChanged(nameof(IsCacheTypeKSupported));
        OnPropertyChanged(nameof(IsCacheTypeVSupported));
        OnPropertyChanged(nameof(IsDraftModelSupported));
        OnPropertyChanged(nameof(IsDraftGpuLayersSupported));
        OnPropertyChanged(nameof(IsSpecDraftNMaxSupported));
        OnPropertyChanged(nameof(IsSpecDraftNMinSupported));
        OnPropertyChanged(nameof(IsSpecDraftPSplitSupported));
        OnPropertyChanged(nameof(IsSpecDraftPMinSupported));
        OnPropertyChanged(nameof(IsHfRepoSupported));
        OnPropertyChanged(nameof(IsHfFileSupported));
        OnPropertyChanged(nameof(IsOfflineSupported));
        OnPropertyChanged(nameof(IsHfRepoDraftSupported));
        OnPropertyChanged(nameof(HasUnsupportedSpecType));
        OnPropertyChanged(nameof(SpecTypeBorderBrush));
        OnPropertyChanged(nameof(SpecTypeToolTip));
        OnPropertyChanged(nameof(HasUnsupportedDraftModel));
        OnPropertyChanged(nameof(HasUnsupportedDraftGpuLayers));
        OnPropertyChanged(nameof(HasUnsupportedSpecDraftNMax));
        OnPropertyChanged(nameof(HasUnsupportedSpecDraftNMin));
        OnPropertyChanged(nameof(HasUnsupportedSpecDraftPSplit));
        OnPropertyChanged(nameof(HasUnsupportedSpecDraftPMin));
        OnPropertyChanged(nameof(DraftModelBorderBrush));
        OnPropertyChanged(nameof(DraftGpuLayersBorderBrush));
        OnPropertyChanged(nameof(SpecDraftNMaxBorderBrush));
        OnPropertyChanged(nameof(SpecDraftNMinBorderBrush));
        OnPropertyChanged(nameof(SpecDraftPSplitBorderBrush));
        OnPropertyChanged(nameof(SpecDraftPMinBorderBrush));
        OnPropertyChanged(nameof(DraftModelToolTip));
        OnPropertyChanged(nameof(DraftGpuLayersToolTip));
        OnPropertyChanged(nameof(SpecDraftNMaxToolTip));
        OnPropertyChanged(nameof(SpecDraftNMinToolTip));
        OnPropertyChanged(nameof(SpecDraftPSplitToolTip));
        OnPropertyChanged(nameof(SpecDraftPMinToolTip));
        OnPropertyChanged(nameof(HasUnsupportedCacheTypeK));
        OnPropertyChanged(nameof(HasUnsupportedCacheTypeV));
        OnPropertyChanged(nameof(CacheTypeKBorderBrush));
        OnPropertyChanged(nameof(CacheTypeVBorderBrush));
        OnPropertyChanged(nameof(CacheTypeKToolTip));
        OnPropertyChanged(nameof(CacheTypeVToolTip));
        OnPropertyChanged(nameof(HasUnsupportedHfRepo));
        OnPropertyChanged(nameof(HasUnsupportedHfFile));
        OnPropertyChanged(nameof(HasUnsupportedOffline));
        OnPropertyChanged(nameof(HasUnsupportedHfRepoDraft));
        OnPropertyChanged(nameof(HfRepoBorderBrush));
        OnPropertyChanged(nameof(HfFileBorderBrush));
        OnPropertyChanged(nameof(HfRepoDraftBorderBrush));
        OnPropertyChanged(nameof(HfRepoToolTip));
        OnPropertyChanged(nameof(HfFileToolTip));
        OnPropertyChanged(nameof(OfflineToolTip));
        OnPropertyChanged(nameof(HfRepoDraftToolTip));
        OnPropertyChanged(nameof(IsHelpAvailable));
    }

    public async Task RefreshSupportedFlagsAsync(bool force = false)
    {
        try
        {
            var exePath = ExecutablePath;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                // Try the default llama-server path when explicit path is not set
                var defaultPath = _downloadService.GetDefaultLlamaServerPath();
                if (!string.IsNullOrEmpty(defaultPath))
                    exePath = defaultPath;
            }

            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                _logService.Warning($"[FlagFilter] llama-server.exe not found at '{exePath}'. Download llama.cpp or set Executable Path to enable flag filtering.");
                _supportedFlags = null;
                _lastCheckedExePath = "";
                _lastHelpText = "";
                UpdateFeatureSupportProperties();
                UpdateSpecTypeOptions();
                UpdateCacheTypeOptions();
                UpdateCurrentCommand();
                return;
            }

            // Avoid re-parsing the same executable, unless forced (e.g. binary was
            // replaced in-place by an update, so the path string is unchanged).
            if (!force && exePath == _lastCheckedExePath && _supportedFlags != null)
                return;

            var result = await LlamaHelpParserService.GetSupportedFlagsWithHelpAsync(exePath);
            if (result != null)
            {
                _supportedFlags = result.Flags;
                _lastHelpText = result.HelpText;
            }
            else
            {
                _logService.Warning($"[FlagFilter] Failed to parse --help output from '{exePath}'. Flag filtering disabled.");
                _supportedFlags = null;
                _lastHelpText = "";
            }
            _lastCheckedExePath = exePath;
            UpdateFeatureSupportProperties();
            UpdateSpecTypeOptions();
            UpdateCacheTypeOptions();
            UpdateCurrentCommand();
        }
        catch (Exception ex)
        {
            _logService.Error($"[FlagFilter] RefreshSupportedFlagsAsync threw: {ex}");
            _supportedFlags = null;
        }
    }

    private string ResolveExecutablePath(string savedPath)
    {
        if (!string.IsNullOrEmpty(savedPath))
        {
            // Use LlamaServerService resolver which handles both absolute paths
            // and bare names resolvable via PATH (e.g. "llama-server")
            var resolved = LlamaServerService.ResolveExecutablePath(savedPath);
            if (!string.IsNullOrEmpty(resolved))
                return resolved;

            // Fall back to the default downloaded llama-server
            var defaultPath = _downloadService.GetDefaultLlamaServerPath();
            if (!string.IsNullOrEmpty(defaultPath) && File.Exists(defaultPath))
            {
                _logService.Info($"[FlagFilter] Saved executable path '{savedPath}' not found, using default '{defaultPath}'");
                return defaultPath;
            }
        }

        return string.Empty;
    }

    private string NormalizeExecutablePathForSaving(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        try
        {
            var defaultPath = _downloadService.GetDefaultLlamaServerPath();
            if (!string.IsNullOrEmpty(defaultPath) &&
                string.Equals(Path.GetFullPath(path), Path.GetFullPath(defaultPath), StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }
        }
        catch
        {
            // Bad/partial path (e.g. mid-typing) - just return it as-is.
        }

        return path;
    }

    private void UpdateSpecTypeOptions()
    {
        var parsedValues = new List<string>();

        if (!string.IsNullOrEmpty(_lastHelpText))
        {
            parsedValues = LlamaHelpParserService.ParseSpecTypeValues(_lastHelpText);
        }

        // Valid values: only what was explicitly parsed from help (for command line validation).
        // When no help text available, treat everything as valid (null = no restriction).
        _validSpecTypeValues = string.IsNullOrEmpty(_lastHelpText) ? new List<string>() : parsedValues;

        // Build the desired UI options list
        var desiredOptions = new List<string> { "" };
        if (parsedValues.Count > 0)
        {
            desiredOptions.AddRange(parsedValues);
        }
        else if (string.IsNullOrEmpty(_lastHelpText))
        {
            // No help text available — use full defaults
            desiredOptions.AddRange(new[] { "none", "draft-simple", "draft-mtp" });
        }

        // Preserve current selection so user can see it's unsupported
        if (!string.IsNullOrEmpty(_specType) && !desiredOptions.Contains(_specType))
        {
            desiredOptions.Add(_specType);
        }

        // Save current value before rebuilding the list
        var savedSpecType = _specType;

        // Suppress SpecType setter to prevent ComboBox TwoWay binding
        // from nulling out the value during the ItemsSource transition
        _suppressSpecTypeChange = true;
        try
        {
            _specTypeOptions = desiredOptions;
            OnPropertyChanged(nameof(SpecTypeOptions));
        }
        finally
        {
            _suppressSpecTypeChange = false;
        }

        if (savedSpecType != null && savedSpecType == _specType)
        {
            var specTypeToReApply = _specType;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_specType == specTypeToReApply)
                {
                    // Flicker: temporarily set to null, notify, then restore.
                    // This forces Avalonia's TwoWay binding to see a source change
                    // and push the real value to the ComboBox's SelectedItem.
                    _specType = string.Empty;
                    OnPropertyChanged(nameof(SpecType));
                    _specType = specTypeToReApply;
                    OnPropertyChanged(nameof(SpecType));
                    OnPropertyChanged(nameof(ShowSpecFields));
                    OnPropertyChanged(nameof(ShowDraftModelFields));
                    OnPropertyChanged(nameof(HasUnsupportedSpecType));
                    OnPropertyChanged(nameof(SpecTypeBorderBrush));
                    OnPropertyChanged(nameof(SpecTypeToolTip));
                    UpdateCurrentCommand();
                }
            });
        }

        OnPropertyChanged(nameof(HasUnsupportedSpecType));
        OnPropertyChanged(nameof(SpecTypeBorderBrush));
        OnPropertyChanged(nameof(SpecTypeToolTip));
    }

    private void UpdateCacheTypeOptions()
    {
        var parsedValues = new List<string>();

        if (!string.IsNullOrEmpty(_lastHelpText))
        {
            parsedValues = LlamaHelpParserService.ParseCacheTypeValues(_lastHelpText);
        }

        // Valid values: only what was explicitly parsed from help (for command line validation).
        _validCacheTypeValues = string.IsNullOrEmpty(_lastHelpText) ? new List<string>() : parsedValues;

        // Build the desired UI options list
        var desiredOptions = new List<string> { "" };
        if (parsedValues.Count > 0)
        {
            desiredOptions.AddRange(parsedValues);
        }
        else if (string.IsNullOrEmpty(_lastHelpText))
        {
            // No help text available — use full defaults
            desiredOptions.AddRange(new[] { "f32", "f16", "bf16", "q8_0", "q4_0", "q4_1", "iq4_nl", "q5_0", "q5_1", "turbo2", "turbo3", "turbo4" });
        }

        // Preserve current selections so user can see they're unsupported
        if (!string.IsNullOrEmpty(_cacheTypeK) && !desiredOptions.Contains(_cacheTypeK))
            desiredOptions.Add(_cacheTypeK);
        if (!string.IsNullOrEmpty(_cacheTypeV) && !desiredOptions.Contains(_cacheTypeV))
            desiredOptions.Add(_cacheTypeV);

        // Suppress setters to prevent ComboBox TwoWay binding from nulling values
        var savedCacheTypeK = _cacheTypeK;
        var savedCacheTypeV = _cacheTypeV;

        _suppressCacheTypeKChange = true;
        _suppressCacheTypeVChange = true;
        try
        {
            _cacheTypeOptions = desiredOptions;
            OnPropertyChanged(nameof(CacheTypeOptions));
        }
        finally
        {
            _suppressCacheTypeKChange = false;
            _suppressCacheTypeVChange = false;
        }

        // Force Avalonia TwoWay binding to re-read the current values
        if (savedCacheTypeK != null && savedCacheTypeK == _cacheTypeK)
        {
            var ctk = _cacheTypeK;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_cacheTypeK == ctk)
                {
                    _cacheTypeK = string.Empty;
                    OnPropertyChanged(nameof(CacheTypeK));
                    _cacheTypeK = ctk;
                    OnPropertyChanged(nameof(CacheTypeK));
                    OnPropertyChanged(nameof(HasUnsupportedCacheTypeK));
                    OnPropertyChanged(nameof(CacheTypeKBorderBrush));
                    OnPropertyChanged(nameof(CacheTypeKToolTip));
                    UpdateCurrentCommand();
                }
            });
        }
        if (savedCacheTypeV != null && savedCacheTypeV == _cacheTypeV)
        {
            var ctv = _cacheTypeV;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_cacheTypeV == ctv)
                {
                    _cacheTypeV = string.Empty;
                    OnPropertyChanged(nameof(CacheTypeV));
                    _cacheTypeV = ctv;
                    OnPropertyChanged(nameof(CacheTypeV));
                    OnPropertyChanged(nameof(HasUnsupportedCacheTypeV));
                    OnPropertyChanged(nameof(CacheTypeVBorderBrush));
                    OnPropertyChanged(nameof(CacheTypeVToolTip));
                    UpdateCurrentCommand();
                }
            });
        }

        OnPropertyChanged(nameof(HasUnsupportedCacheTypeK));
        OnPropertyChanged(nameof(CacheTypeKBorderBrush));
        OnPropertyChanged(nameof(CacheTypeKToolTip));
        OnPropertyChanged(nameof(HasUnsupportedCacheTypeV));
        OnPropertyChanged(nameof(CacheTypeVBorderBrush));
        OnPropertyChanged(nameof(CacheTypeVToolTip));
    }

    public LlamaCppDownloadService DownloadService => _downloadService;

    public Dictionary<string, string> ReleaseBodyCache => _releaseBodyCache;
    public List<string> ReleaseBodyCacheOrder => _releaseBodyCacheOrder;

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
            
            var loadedProfileRunning = RunningInstances.Any(i => i.ProfileName == _loadedProfileName && i.IsRunning);
            if (loadedProfileRunning)
            {
                title += $" - {Localized.WindowTitleRunning}";
            }
            
            return title;
        }
    }

    public string SelectedProfile
    {
        get => _selectedProfile;
        set { _selectedProfile = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnsavedChanges)); OnPropertyChanged(nameof(WindowTitleWithProfile)); }
    }

    private void SetSelectedProfileSilently(string name)
    {
        _suppressProfileAutoLoad = true;
        try { SelectedProfile = name; }
        finally { _suppressProfileAutoLoad = false; }
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

    private static bool ToggleStatesEqual(Dictionary<string, bool>? a, Dictionary<string, bool>? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        if (a.Count != b.Count) return false;
        foreach (var kvp in a)
        {
            if (!b.TryGetValue(kvp.Key, out var val) || val != kvp.Value)
                return false;
        }
        return true;
    }

    private bool ConfigsEqual(ServerConfiguration a, ServerConfiguration b)
    {
        // Explicit default path and empty path both mean "use default" (saving
        // normalizes one to the other), so compare in normalized form; otherwise
        // a typed-out default path would never clear the unsaved indicator.
        return NormalizeExecutablePathForSaving(a.ExecutablePath ?? string.Empty)
                    == NormalizeExecutablePathForSaving(b.ExecutablePath ?? string.Empty) &&
                a.ModelPath == b.ModelPath &&
                a.ModelsDir == b.ModelsDir &&
                a.Host == b.Host &&
                a.Port == b.Port &&
                a.ContextSize == b.ContextSize &&
                a.Threads == b.Threads &&
                a.GpuLayers == b.GpuLayers &&
                a.CpuMoe == b.CpuMoe &&
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
            a.ParallelSlots == b.ParallelSlots &&
            a.ContBatching == b.ContBatching &&
            a.Timeout == b.Timeout &&
            a.CachePrompt == b.CachePrompt &&
            a.Mlock == b.Mlock &&
            a.Mmap == b.Mmap &&
            a.Reasoning == b.Reasoning &&
            a.ReasoningBudget == b.ReasoningBudget &&
            a.Seed == b.Seed &&
            a.PresencePenalty == b.PresencePenalty &&
            a.FrequencyPenalty == b.FrequencyPenalty &&
            a.ContextShift == b.ContextShift &&
            a.HfRepo == b.HfRepo &&
            a.HfFile == b.HfFile &&
            a.Offline == b.Offline &&
            a.HfRepoDraft == b.HfRepoDraft &&
            a.RunInDocker == b.RunInDocker &&
            a.DockerImage == b.DockerImage &&
            a.DockerGpuAll == b.DockerGpuAll &&
            a.DockerRm == b.DockerRm &&
            a.DockerContainerName == b.DockerContainerName &&
            a.CustomArguments == b.CustomArguments &&
            ToggleStatesEqual(a.CustomArgumentToggleStates, b.CustomArgumentToggleStates);
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
                OnPropertyChanged(nameof(CanOpenInBrowser));
                OnPropertyChanged(nameof(CanStartServer));
                // Notify commands that depend on IsServerRunning
                if (StopServerCommand is AsyncRelayCommand stopCmd)
                    stopCmd.RaiseCanExecuteChanged();
                if (RestartServerCommand is AsyncRelayCommand restartCmd)
                    restartCmd.RaiseCanExecuteChanged();
                if (UnloadModelCommand is AsyncRelayCommand unloadCmd)
                    unloadCmd.RaiseCanExecuteChanged();
                if (OpenInBrowserCommand is AsyncRelayCommand openCmd)
                    openCmd.RaiseCanExecuteChanged();
                if (StartServerCommand is AsyncRelayCommand startCmd)
                    startCmd.RaiseCanExecuteChanged();
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

    public bool ScenariosEnabled
    {
        get => _scenariosEnabled;
        set
        {
            if (_scenariosEnabled != value)
            {
                _scenariosEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public string SelectedScenario
    {
        get => _selectedScenario;
        set
        {
            if (_selectedScenario != value)
            {
                _selectedScenario = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedScenario));
                UpdateSelectedScenarioAutoStart();
            }
        }
    }

    public bool HasSelectedScenario => !string.IsNullOrEmpty(_selectedScenario);

    public bool SelectedScenarioAutoStart
    {
        get => _selectedScenarioAutoStart;
        set
        {
            if (_selectedScenarioAutoStart != value)
            {
                _selectedScenarioAutoStart = value;
                OnPropertyChanged();
            }
        }
    }

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

    public bool ShowServerStartError
    {
        get => _showServerStartError;
        private set
        {
            if (_showServerStartError != value)
            {
                _showServerStartError = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LogBorderBrush));
                OnPropertyChanged(nameof(LogBorderThickness));
            }
        }
    }

    public Avalonia.Media.IBrush LogBorderBrush => ShowServerStartError ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.Red) : _transparentBrush;
    public Avalonia.Thickness LogBorderThickness => ShowServerStartError ? new Avalonia.Thickness(2) : new Avalonia.Thickness(0);

    public ICommand BrowseExecutableCommand { get; }
    public ICommand BrowseModelCommand { get; }
    public ICommand BrowseModelsDirCommand { get; }
    public ICommand BrowseLogFileCommand { get; }
    public ICommand BrowseMmprojCommand { get; }
    public ICommand BrowseDraftModelCommand { get; }
    public ICommand BrowseBrowserCommand { get; }
    public ICommand StartServerCommand { get; }
    public ICommand RestartServerCommand { get; }
    public ICommand StopServerCommand { get; }
    public ICommand UnloadModelCommand { get; }
    public ICommand OpenInBrowserCommand { get; }
    public ICommand SaveProfileCommand { get; }
    public ICommand SaveProfileAsCommand { get; }
    public ICommand DeleteProfileCommand { get; }
    public ICommand RenameProfileCommand { get; }
    public ICommand CloneProfileCommand { get; }
    public ICommand ExportProfileCommand { get; }
    public ICommand ExportToBatCommand { get; }
    public ICommand ImportProfileCommand { get; }
    public ICommand ExportAllCommand { get; }
    public ICommand ImportAllCommand { get; }
    public ICommand ClearAllFieldsCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand CopyLogCommand { get; }
    public ICommand SaveLogCommand { get; }
    public ICommand ShowWindowCommand { get; set; }
    public ICommand CloseFromTrayCommand { get; set; }
    public ICommand OpenArgumentPickerCommand { get; }
    public ICommand OpenOptimizerCommand { get; }
    public ICommand RestartLogStreamCommand { get; }
    public ICommand RestartOnDemandProxyCommand { get; }

    public ICommand RunScenarioCommand { get; }
    public ICommand EditScenarioCommand { get; }
    public ICommand CreateScenarioCommand { get; }
    public ICommand DeleteScenarioCommand { get; }

    /// <summary>
    /// Set by MainWindow to allow the ViewModel to request opening the download dialog.
    /// </summary>
    public Func<Task>? OpenDownloadDialogFunc { get; set; }

    public bool HasAnyRunningInstances => RunningInstances.Any(i => i.IsRunning);
    // Includes failed (not running) instances too, so their toggle buttons stay visible.
    public bool HasAnyInstances => RunningInstances.Count > 0;
    public bool HasCustomArgumentItems => CustomArgumentItems.Count > 0;

    public event Action? RequestTrayMenuRebuild;

    public async Task StopAllInstancesAsync()
    {
        var tasks = RunningInstances.ToList().Select(async i =>
        {
            try { await i.StopAsync(); }
            catch (Exception ex)
            {
                _logService.Error($"Failed to stop instance '{i.ProfileName}': {ex.Message}");
            }
        });
        await Task.WhenAll(tasks);

        // Drop failed instances that stopping won't clear by itself.
        foreach (var failed in RunningInstances.Where(i => i.StartFailed && !i.IsRunning).ToList())
            RemoveInstance(failed);
    }

    public bool CanStartServer =>
        !RunningInstances.Any(i => i.IsBusy && i.ProfileName == (_loadedProfileName ?? SelectedProfile))
        && (!string.IsNullOrEmpty(ModelPath) || !string.IsNullOrEmpty(ModelsDir) || !string.IsNullOrEmpty(HfRepo) || !string.IsNullOrEmpty(HfFile))
        && (RunInDocker ? IsDockerAvailable : true)
        && IsPortValid && IsHostValid;

    public bool CanOpenInBrowser => (_selectedInstance?.IsRunning ?? false) && _selectedInstance.Configuration.EnableWebUI != false;

    private async Task BrowseExecutableAsync()
    {
        var result = await WindowsFileDialogs.OpenFileDialogAsync(
            "Select llama-server executable",
            new[] { ("All files", new[] { "*" }) },
            false);
        if (result != null && result.Length > 0)
        {
            ExecutablePath = result[0];
        }
    }

    private void UpdateCurrentCommand()
    {
        var config = GetCurrentConfig();
        CurrentCommand = CommandLineBuilder.BuildFullCommand(config, _supportedFlags, _validSpecTypeValues, _validCacheTypeValues);
    }

    private async Task BrowseModelAsync()
    {
        var modelPatterns = new List<string> { "*.gguf" };
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            modelPatterns.Add("*.safetensors");
            modelPatterns.Add("*.bin");
            modelPatterns.Add("*.mlx");
        }

        var result = await WindowsFileDialogs.OpenFileDialogAsync(
            "Select Model File",
            new[] { ("Model files", modelPatterns.ToArray()), ("All files", new[] { "*.*" }) },
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
            new[] { ("Log files", new[] { "*.log" }), ("All files", new[] { "*.*" }) });
        if (!string.IsNullOrEmpty(result))
        {
            LogFilePath = result;
        }
    }

    private async Task BrowseMmprojAsync()
    {
        var result = await WindowsFileDialogs.OpenFileDialogAsync(
            "Select MMProj File",
            new[] { ("GGUF files", new[] { "*.gguf" }), ("All files", new[] { "*.*" }) },
            false);
        if (result != null && result.Length > 0)
        {
            MmprojPath = result[0];
        }
    }

    private async Task BrowseDraftModelAsync()
    {
        var modelPatterns = new List<string> { "*.gguf" };
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            modelPatterns.Add("*.safetensors");
            modelPatterns.Add("*.bin");
            modelPatterns.Add("*.mlx");
        }

        var result = await WindowsFileDialogs.OpenFileDialogAsync(
            "Select Draft Model File",
            new[] { ("Model files", modelPatterns.ToArray()), ("All files", new[] { "*.*" }) },
            false);
        if (result != null && result.Length > 0)
        {
            SpecDraftModel = result[0];
        }
    }

    private async Task BrowseBrowserAsync()
    {
        var result = await WindowsFileDialogs.OpenFileDialogAsync(
            "Select Browser Executable",
            new[] { ("Executable files", new[] { "*.exe" }), ("All files", new[] { "*.*" }) },
            false);
        if (result != null && result.Length > 0)
        {
            CustomBrowserPath = result[0];
        }
    }

    public async Task OpenArgumentPickerAsync()
    {
        if (_supportedFlags == null || string.IsNullOrWhiteSpace(_lastHelpText))
        {
            if (ShowMessageFunc != null)
                await ShowMessageFunc(LocalizedStrings.Instance.ErrorTitle, LocalizedStrings.Instance.NoHelpAvailable, "error");
            return;
        }

        var allArgs = LlamaHelpParserService.ParseArgumentDescriptions(_lastHelpText);

        var existingCustomArgs = new HashSet<string>(CustomArgumentItems.Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
        var knownFlags = new HashSet<string>(ServerConfiguration.KnownArguments.Keys, StringComparer.OrdinalIgnoreCase);
        var excludedFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "-h", "--help", "--version", "--license", "--usage"
        };

        var filtered = allArgs
            .Where(a => _supportedFlags.Contains(a.PrimaryFlag))
            .Where(a => !knownFlags.Contains(a.PrimaryFlag))
            .Where(a => !existingCustomArgs.Contains(a.PrimaryFlag))
            .Where(a => !a.AllFlags.Any(f => knownFlags.Contains(f)))
            .Where(a => !a.AllFlags.Any(f => existingCustomArgs.Contains(f)))
            .Where(a => !excludedFlags.Contains(a.PrimaryFlag))
            .Where(a => !a.AllFlags.Any(f => excludedFlags.Contains(f)))
            .ToList();

        var dialog = new ArgumentPickerWindow();
        var vm = new ArgumentPickerViewModel(filtered);
        dialog.SetViewModel(vm, _configService, DialogGeometryDict);
        await dialog.ShowDialog(MainWindow.Instance!);

        if (dialog.CapturedGeometry != null) { DialogGeometryDict["ArgumentPicker"] = dialog.CapturedGeometry; await SaveSettingsAsync(); }

        if (!dialog.IsConfirmed) return;

        var selected = dialog.SelectedArguments;
        if (selected == null || selected.Count == 0) return;

        var additions = new List<string>();
        foreach (var item in selected)
        {
            if (!string.IsNullOrEmpty(item.DefaultValue))
                additions.Add($"{item.PrimaryFlag} {item.DefaultValue}");
            else
                additions.Add(item.PrimaryFlag);
        }

        var newArgs = string.Join(" ", additions);
        if (!string.IsNullOrWhiteSpace(CustomArguments))
            CustomArguments = CustomArguments.Trim() + " " + newArgs;
        else
            CustomArguments = newArgs;

        ParseAndUpdateCustomArguments();
    }

    public void OpenOptimizer()
    {
        var vm = new OptimizationViewModel(
            ModelPath, ExecutablePath, ApplyOptimizationResult, _logService,
            contextSize: ContextSize, cacheTypeK: CacheTypeK, cacheTypeV: CacheTypeV, mmprojPath: MmprojPath);
        var win = new LlamaServerLauncher.OptimizationWindow();
        win.SetViewModel(vm, DialogGeometryDict);
        win.Closed += async (_, _) =>
        {
            if (win.CapturedGeometry != null)
            {
                DialogGeometryDict["Optimization"] = win.CapturedGeometry;
                await SaveSettingsAsync();
            }
        };
        win.Show(MainWindow.Instance!);
    }

    private bool ApplyOptimizationResult(Models.Optimization.OptimizationResult r)
    {
        if (!r.ImprovedOverBaseline)
        {
            Toasts.Show(LocalizedStrings.Instance.OptNoImprovementApply);
            return false;
        }

        Threads = r.Threads.ToString();
        if (r.GpuLayers is { } ngl)
            GpuLayers = ngl.ToString();
        if (r.NCpuMoe is { } ncmoe)
            CpuMoe = ncmoe.ToString();
        BatchSize = r.BatchSize.ToString();
        UBatchSize = r.UBatchSize.ToString();
        FlashAttention = r.FlashAttn;
        if (r.RecommendedCtxSize is { } c)
            ContextSize = c.ToString();
        if (!string.IsNullOrEmpty(r.OverridePattern))
            InjectOverrideTensor(r.OverridePattern!);
        Toasts.Show(LocalizedStrings.Instance.OptApplied);
        return true;
    }

    private void InjectOverrideTensor(string pattern)
    {
        string token = $"--override-tensor \"{pattern}\"";
        string existing = CustomArguments ?? string.Empty;
        var rx = new System.Text.RegularExpressions.Regex("(?:--override-tensor|-ot)\\s+(?:\"[^\"]*\"|\\S+)");
        if (rx.IsMatch(existing))
            CustomArguments = rx.Replace(existing, token, 1);
        else
            CustomArguments = string.IsNullOrWhiteSpace(existing) ? token : existing.Trim() + " " + token;
        ParseAndUpdateCustomArguments();
    }

    public ServerConfiguration GetCurrentConfig()
    {
        return new ServerConfiguration
        {
            ExecutablePath = ExecutablePath,
            ModelPath = ModelPath,
            ModelsDir = ModelsDir,
            Host = Host,
            Port = ParseNullableInt(Port) ?? 8080,
            ContextSize = ParseNullableInt(ContextSize),
            Threads = ParseNullableInt(Threads),
            GpuLayers = ParseNullableInt(GpuLayers),
            CpuMoe = ParseNullableInt(CpuMoe),
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
            CustomArguments = string.Join(" ", CustomArgumentItems.Select(x => x.OriginalArg)),
            CustomArgumentToggleStates = GetToggleStates(),
            ParallelSlots = ParseNullableInt(ParallelSlots),
            ContBatching = ContBatching,
            Timeout = ParseNullableInt(Timeout),
            CachePrompt = CachePrompt,
            Mlock = Mlock,
            Mmap = Mmap,
            Reasoning = Reasoning,
            ReasoningBudget = ParseNullableInt(ReasoningBudget),
            Seed = ParseNullableInt(Seed),
            PresencePenalty = ParseNullableDouble(PresencePenalty),
            FrequencyPenalty = ParseNullableDouble(FrequencyPenalty),
            ContextShift = ContextShift,
            SpecType = SpecType,
            SpecDraftModel = SpecDraftModel,
            SpecDraftGpuLayers = SpecDraftGpuLayers,
            SpecDraftNMax = ParseNullableInt(SpecDraftNMax),
            SpecDraftNMin = ParseNullableInt(SpecDraftNMin),
            SpecDraftPSplit = ParseNullableDouble(SpecDraftPSplit),
            SpecDraftPMin = ParseNullableDouble(SpecDraftPMin),
            HfRepo = HfRepo,
            HfFile = HfFile,
            Offline = Offline,
            HfRepoDraft = HfRepoDraft,
            RunInDocker = RunInDocker,
            DockerImage = DockerImage,
            DockerGpuAll = DockerGpuAll,
            DockerRm = DockerRm,
            DockerContainerName = DockerContainerName
        };
    }

private void LoadConfigToUI(ServerConfiguration config)
    {
        _isLoadingConfig = true;
        try
        {
            _shownConflictToasts.Clear();
            Toasts.ClearAll();
        // Clear all fields except profile-related fields
        ExecutablePath = string.Empty;
        ModelPath = string.Empty;
        ModelsDir = string.Empty;
        Host = "127.0.0.1";
        Port = "8080";
        ContextSize = string.Empty;
        Threads = string.Empty;
        GpuLayers = string.Empty;
        CpuMoe = string.Empty;
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
        ParallelSlots = string.Empty;
        ContBatching = null;
        Timeout = string.Empty;
        CachePrompt = null;
        Mlock = null;
        Mmap = null;
        Reasoning = null;
        ReasoningBudget = string.Empty;
        Seed = string.Empty;
        PresencePenalty = string.Empty;
        FrequencyPenalty = string.Empty;
        ContextShift = null;
        SpecType = string.Empty;
        SpecDraftModel = string.Empty;
        SpecDraftGpuLayers = string.Empty;
        SpecDraftNMax = string.Empty;
        SpecDraftNMin = string.Empty;
        SpecDraftPSplit = string.Empty;
        SpecDraftPMin = string.Empty;
        HfRepo = string.Empty;
        HfFile = string.Empty;
        Offline = false;
        HfRepoDraft = string.Empty;
        RunInDocker = false;
        DockerImage = "ghcr.io/ggml-org/llama.cpp:server";
        DockerGpuAll = false;
        DockerRm = true;
        DockerContainerName = string.Empty;
        CustomArguments = string.Empty;
        // Note: AutoRestart is runtime state per-instance; do NOT reset it here.
        // Note: ProfileNameInput and _loadedProfileName are NOT cleared here
        // They are preserved when loading a profile
        
        // Then load the new config values
        ExecutablePath = config.ExecutablePath ?? string.Empty;
        ModelPath = config.ModelPath ?? string.Empty;
        ModelsDir = config.ModelsDir ?? string.Empty;
        Host = config.Host ?? "127.0.0.1";
        Port = config.Port.ToString();
        ContextSize = config.ContextSize?.ToString() ?? string.Empty;
        Threads = config.Threads?.ToString() ?? string.Empty;
        GpuLayers = config.GpuLayers?.ToString() ?? string.Empty;
        CpuMoe = config.CpuMoe?.ToString() ?? string.Empty;
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
        ParallelSlots = config.ParallelSlots?.ToString() ?? string.Empty;
        ContBatching = config.ContBatching;
        Timeout = config.Timeout?.ToString() ?? string.Empty;
        CachePrompt = config.CachePrompt;
        Mlock = config.Mlock;
        Mmap = config.Mmap;
        Reasoning = config.Reasoning;
        ReasoningBudget = config.ReasoningBudget?.ToString() ?? string.Empty;
        Seed = config.Seed?.ToString() ?? string.Empty;
        PresencePenalty = config.PresencePenalty?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        FrequencyPenalty = config.FrequencyPenalty?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        ContextShift = config.ContextShift;
        SpecType = config.SpecType ?? string.Empty;
        SpecDraftModel = config.SpecDraftModel ?? string.Empty;
        SpecDraftGpuLayers = config.SpecDraftGpuLayers ?? string.Empty;
        SpecDraftNMax = config.SpecDraftNMax?.ToString() ?? string.Empty;
        SpecDraftNMin = config.SpecDraftNMin?.ToString() ?? string.Empty;
        SpecDraftPSplit = config.SpecDraftPSplit?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        SpecDraftPMin = config.SpecDraftPMin?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        HfRepo = config.HfRepo ?? string.Empty;
        HfFile = config.HfFile ?? string.Empty;
        Offline = config.Offline;
        HfRepoDraft = config.HfRepoDraft ?? string.Empty;
        RunInDocker = config.RunInDocker;
        DockerImage = config.DockerImage ?? "ghcr.io/ggml-org/llama.cpp:server";
        DockerGpuAll = config.DockerGpuAll;
        DockerRm = config.DockerRm;
        DockerContainerName = config.DockerContainerName ?? string.Empty;
        _disabledArguments.Clear();
        _originalCustomArguments = string.Empty;
        CustomArguments = config.CustomArguments ?? string.Empty;
        ParseCustomArguments();
        if (config.CustomArgumentToggleStates != null && config.CustomArgumentToggleStates.Count > 0)
        {
            ApplyToggleStates(config.CustomArgumentToggleStates);
        }
        RebuildCustomArgumentsFromToggles();
        }
        finally
        {
            _isLoadingConfig = false;
            UpdateModelLoadModeStatus();

            var savedPath = config.ExecutablePath ?? string.Empty;
            if (!string.IsNullOrEmpty(savedPath) && LlamaServerService.ResolveExecutablePath(savedPath) == null && !File.Exists(savedPath))
            {
                var defaultPath = _downloadService.GetDefaultLlamaServerPath();
                if (!string.IsNullOrEmpty(defaultPath))
                {
                    Toasts.Show(
                        string.Format(LocalizedStrings.GetString("ToastSavedExecutableNotFound"), savedPath),
                        durationMs: 15000,
                        onClick: () =>
                        {
                            ExecutablePath = defaultPath;
                            _ = RefreshSupportedFlagsAsync();
                        });
                }
            }
        }
    }

    public void ClearAllFields()
    {
        _isLoadingConfig = true;
        try
        {
            ExecutablePath = string.Empty;
        ModelPath = string.Empty;
        ModelsDir = string.Empty;
        Host = "127.0.0.1";
        Port = "8080";
        ContextSize = string.Empty;
        Threads = string.Empty;
        GpuLayers = string.Empty;
        CpuMoe = string.Empty;
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
        ParallelSlots = string.Empty;
        ContBatching = null;
        Timeout = string.Empty;
        CachePrompt = null;
        Mlock = null;
        Mmap = null;
        Reasoning = null;
        ReasoningBudget = string.Empty;
        Seed = string.Empty;
        PresencePenalty = string.Empty;
        FrequencyPenalty = string.Empty;
        ContextShift = null;
        SpecType = string.Empty;
        SpecDraftModel = string.Empty;
        SpecDraftGpuLayers = string.Empty;
        SpecDraftNMax = string.Empty;
        SpecDraftNMin = string.Empty;
        SpecDraftPSplit = string.Empty;
        SpecDraftPMin = string.Empty;
        HfRepo = string.Empty;
        HfFile = string.Empty;
        Offline = false;
        HfRepoDraft = string.Empty;
        RunInDocker = false;
        DockerImage = "ghcr.io/ggml-org/llama.cpp:server";
        DockerGpuAll = false;
        DockerRm = true;
        DockerContainerName = string.Empty;
        _disabledArguments.Clear();
        _originalCustomArguments = string.Empty;
        CustomArgumentItems.Clear();
        CustomArguments = string.Empty;
        AutoRestart = false;
        // ProfileNameInput = string.Empty;
        _loadedProfileName = string.Empty;
        _loadedProfileConfig = null;
        _shownConflictToasts.Clear();
        OnPropertyChanged(nameof(HasUnsavedChanges));
        }
        finally
        {
            _isLoadingConfig = false;
            UpdateModelLoadModeStatus();
        }
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
            bool isFlag = CommandLineParser.IsFlag(token);

            if (isFlag && i + 1 < tokens.Count)
            {
                var next = tokens[i + 1];
                bool nextIsFlag = CommandLineParser.IsFlag(next);
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
            bool isFlag = CommandLineParser.IsFlag(token);

            if (isFlag && i + 1 < tokens.Count)
            {
                var next = tokens[i + 1];
                bool nextIsFlag = CommandLineParser.IsFlag(next);
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

    public void AddRecentValue(string fieldName, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!_recentValues.ContainsKey(fieldName))
            _recentValues[fieldName] = new List<string>();
        var list = _recentValues[fieldName];
        list.Remove(value);
        list.Insert(0, value);
        while (list.Count > MaxRecentValues)
            list.RemoveAt(list.Count - 1);
    }

    public List<string> GetRecentValues(string fieldName)
    {
        if (_recentValues.TryGetValue(fieldName, out var list))
            return list;
        return new List<string>();
    }

    private void RecordCurrentValuesToHistory()
    {
        AddRecentValue(nameof(ExecutablePath), ExecutablePath);
        AddRecentValue(nameof(ModelPath), ModelPath);
        AddRecentValue(nameof(ModelsDir), ModelsDir);
        AddRecentValue(nameof(Host), Host);
        AddRecentValue(nameof(Port), Port);
        AddRecentValue(nameof(ContextSize), ContextSize);
        AddRecentValue(nameof(Threads), Threads);
        AddRecentValue(nameof(GpuLayers), GpuLayers);
        AddRecentValue(nameof(Temperature), Temperature);
        AddRecentValue(nameof(MaxTokens), MaxTokens);
        AddRecentValue(nameof(BatchSize), BatchSize);
        AddRecentValue(nameof(UBatchSize), UBatchSize);
        AddRecentValue(nameof(MinP), MinP);
        AddRecentValue(nameof(TopK), TopK);
        AddRecentValue(nameof(TopP), TopP);
        AddRecentValue(nameof(RepeatPenalty), RepeatPenalty);
        AddRecentValue(nameof(Seed), Seed);
        AddRecentValue(nameof(PresencePenalty), PresencePenalty);
        AddRecentValue(nameof(FrequencyPenalty), FrequencyPenalty);
        AddRecentValue(nameof(ApiKey), ApiKey);
        AddRecentValue(nameof(Alias), Alias);
        AddRecentValue(nameof(LogFilePath), LogFilePath);
        AddRecentValue(nameof(MmprojPath), MmprojPath);
        AddRecentValue(nameof(ParallelSlots), ParallelSlots);
        AddRecentValue(nameof(Timeout), Timeout);
        AddRecentValue(nameof(ReasoningBudget), ReasoningBudget);
        AddRecentValue(nameof(SpecDraftModel), SpecDraftModel);
        AddRecentValue(nameof(SpecDraftGpuLayers), SpecDraftGpuLayers);
        AddRecentValue(nameof(SpecDraftNMax), SpecDraftNMax);
        AddRecentValue(nameof(SpecDraftNMin), SpecDraftNMin);
        AddRecentValue(nameof(SpecDraftPSplit), SpecDraftPSplit);
        AddRecentValue(nameof(SpecDraftPMin), SpecDraftPMin);
        AddRecentValue(nameof(HfRepo), HfRepo);
        AddRecentValue(nameof(HfFile), HfFile);
        AddRecentValue(nameof(HfRepoDraft), HfRepoDraft);
    }

    private async Task StartServerAsync()
    {
        try
        {
            DismissServerStartError();
            var config = GetCurrentConfig();

            if (!config.RunInDocker)
            {
                if (string.IsNullOrEmpty(config.ExecutablePath))
                {
                    var defaultPath = _downloadService.GetDefaultLlamaServerPath();
                    if (defaultPath != null)
                    {
                        config.ExecutablePath = defaultPath;
                    }
                    else
                    {
                        var prompt = LocalizedStrings.Instance.PromptDownloadLlama;
                        var result = await MessageBox.ShowAsync(
                            MainWindow.Instance!,
                            prompt,
                            LocalizedStrings.Instance.ConfirmTitle,
                            MessageBoxButtons.YesNoCancel,
                            MessageBoxIcon.Question);

                        if (result == MessageBoxResult.Yes)
                        {
                            if (OpenDownloadDialogFunc != null)
                                await OpenDownloadDialogFunc();
                            return;
                        }
                        return;
                    }
                }
            }
            else
            {
                if (!_isDockerAvailable)
                    throw new InvalidOperationException(LocalizedStrings.Instance.DockerNotInstalledError);
            }

            var profileName = !string.IsNullOrWhiteSpace(_loadedProfileName)
                ? _loadedProfileName
                : (!string.IsNullOrWhiteSpace(ProfileNameInput) ? ProfileNameInput : "Unnamed");

            var runningExisting = RunningInstances.FirstOrDefault(i => i.ProfileName == profileName && i.IsRunning);
            if (runningExisting != null)
            {
                var existingJson = JsonSerializer.Serialize(runningExisting.Configuration);
                var newJson = JsonSerializer.Serialize(config);
                if (existingJson != newJson)
                {
                    await ShowWarningAsync($"Profile '{profileName}' is already running with different settings. Stop it first to apply new configuration.");
                    SelectedInstance = runningExisting;
                    return;
                }
                SelectedInstance = runningExisting;
                return;
            }

            RecordCurrentValuesToHistory();
            await RefreshSupportedFlagsAsync();

            await LaunchInstanceAsync(profileName, config);
        }
        catch (Exception ex)
        {
            var message = string.Format(LocalizedStrings.Instance.FailedToStartServer, ex.Message);
            await ShowErrorAsync(message);
        }
    }

    private void OnInstanceRequestRemove(ServerInstance instance)
    {
        Dispatcher.UIThread.Post(() => RemoveInstance(instance));
    }

    /// <summary>
    /// Removes a failed (not running) instance's toggle button from the panel.
    /// Used when the user dismisses a server that failed to start.
    /// </summary>
    public void DismissInstance(ServerInstance instance)
    {
        Dispatcher.UIThread.Post(() => RemoveInstance(instance));
    }

    private void RemoveInstance(ServerInstance instance)
    {
        {
            if (!RunningInstances.Contains(instance))
                return;
            instance.ServerStateChanged -= OnInstanceServerStateChanged;
            instance.RequestRemove -= OnInstanceRequestRemove;
            instance.PropertyChanged -= OnInstancePropertyChanged;
            RunningInstances.Remove(instance);
            if (SelectedInstance == instance)
            {
                SelectedInstance = RunningInstances.LastOrDefault();
                if (SelectedInstance != null)
                {
                    if (!HasUnsavedChanges)
                    {
                        LoadConfigToUI(SelectedInstance.Configuration);
                        _loadedProfileName = SelectedInstance.ProfileName;
                        _loadedProfileConfig = SelectedInstance.Configuration;
                        SetSelectedProfileSilently(SelectedInstance.ProfileName);
                        OnPropertyChanged(nameof(HasUnsavedChanges));
                        OnPropertyChanged(nameof(WindowTitleWithProfile));
                    }
                }
                else
                {
                    ServerStatus = Localized.StatusStopped;
                }
            }
            instance.Dispose();
            RequestTrayMenuRebuild?.Invoke();
        }
    }

    private async void OnInstanceServerStateChanged(object? sender, bool isRunning)
    {
        if (sender is not ServerInstance instance) return;
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!isRunning && instance.ShowServerStartError)
                {
                    var msg = string.Format(LocalizedStrings.GetString("ServerStartFailedToast"), instance.ProfileName);
                    Toasts.ShowError(msg);
                }

                if (SelectedInstance == instance)
                {
                    IsServerRunning = isRunning;
                    ServerStatus = isRunning
                        ? string.Format(Resources.LocalizedStrings.GetString("StatusRunning"), instance.ProcessId)
                        : Localized.StatusStopped;

                    if (!isRunning && instance.ShowServerStartError)
                        ShowServerStartError = true;
                    else if (isRunning)
                        DismissServerStartError();
                }
                OnPropertyChanged(nameof(HasAnyRunningInstances));
            });
        }
        catch (TaskCanceledException) { }
    }

    private void OnInstancePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ServerInstance instance) return;

        if (e.PropertyName == nameof(ServerInstance.AutoRestart))
        {
            if (instance == _selectedInstance)
            {
                _autoRestart = instance.AutoRestart;
                OnPropertyChanged(nameof(AutoRestart));
            }
            RequestTrayMenuRebuild?.Invoke();
        }
        else if (e.PropertyName == nameof(ServerInstance.LogEnabled))
        {
            if (instance == _selectedInstance)
            {
                _logEnabled = instance.LogEnabled;
                OnPropertyChanged(nameof(LogEnabled));
                OnPropertyChanged(nameof(ToggleLogButtonText));
            }
            RequestTrayMenuRebuild?.Invoke();
        }
        else if (e.PropertyName == nameof(ServerInstance.ShowServerStartError) && instance == _selectedInstance)
        {
            ShowServerStartError = instance.ShowServerStartError;
        }
    }

    public async Task SelectInstanceAsync(ServerInstance instance)
    {
        if (_selectedInstance == instance)
        {
            // Even if already selected, check for unsaved changes before reloading
            if (HasUnsavedChanges)
            {
                var result = await ShowConfirmAsync(
                    LocalizedStrings.Instance.UnsavedChangesMessage,
                    LocalizedStrings.Instance.UnsavedChangesTitle);

                if (result == MessageBoxResult.Cancel)
                    return;

                if (result == MessageBoxResult.Yes)
                {
                    var saveName = _loadedProfileName;
                    if (string.IsNullOrWhiteSpace(saveName))
                        saveName = ProfileNameInput;
                    if (string.IsNullOrWhiteSpace(saveName))
                    {
                        await ShowWarningAsync(LocalizedStrings.Instance.PleaseEnterProfileName);
                        return;
                    }
                    var saveConfig = GetCurrentConfig();
                    await _configService.SaveProfileAsync(saveName, saveConfig);
                    ProfileNameInput = string.Empty;
                    LoadProfiles();
                    SetSelectedProfileSilently(saveName);
                    _loadedProfileName = saveName;
                    _loadedProfileConfig = saveConfig;

                    var saveMatch = RunningInstances.FirstOrDefault(i => i.ProfileName == saveName);
                    if (saveMatch != null)
                    {
                        var instanceConfig = GetCurrentConfig();
                        if (!instanceConfig.RunInDocker && string.IsNullOrEmpty(instanceConfig.ExecutablePath))
                        {
                            var dp = _downloadService.GetDefaultLlamaServerPath();
                            if (dp != null) instanceConfig.ExecutablePath = dp;
                        }
                        saveMatch.UpdateConfiguration(instanceConfig);
                    }

                    OnPropertyChanged(nameof(HasUnsavedChanges));
                }
            }

            // Reload the cached configuration from the instance
            if (instance != null)
            {
                LoadConfigToUI(instance.Configuration);
                _loadedProfileName = instance.ProfileName;
                _loadedProfileConfig = instance.Configuration;
                SetSelectedProfileSilently(instance.ProfileName);

                // Sync runtime state (AutoRestart, LogEnabled) to control panel
                _autoRestart = instance.AutoRestart;
                _logEnabled = instance.LogEnabled;
                OnPropertyChanged(nameof(AutoRestart));
                OnPropertyChanged(nameof(LogEnabled));
                OnPropertyChanged(nameof(ToggleLogButtonText));

                OnPropertyChanged(nameof(HasUnsavedChanges));
                OnPropertyChanged(nameof(WindowTitleWithProfile));
            }
            return;
        }

        if (HasUnsavedChanges)
        {
            var result = await ShowConfirmAsync(
                LocalizedStrings.Instance.UnsavedChangesMessage,
                LocalizedStrings.Instance.UnsavedChangesTitle);

            if (result == MessageBoxResult.Cancel)
                return;

            if (result == MessageBoxResult.Yes)
            {
                var saveName = _loadedProfileName;
                if (string.IsNullOrWhiteSpace(saveName))
                    saveName = ProfileNameInput;
                if (string.IsNullOrWhiteSpace(saveName))
                {
                    await ShowWarningAsync(LocalizedStrings.Instance.PleaseEnterProfileName);
                    return;
                }
                var config = GetCurrentConfig();
                await _configService.SaveProfileAsync(saveName, config);
                ProfileNameInput = string.Empty;
                LoadProfiles();
                SetSelectedProfileSilently(saveName);
                _loadedProfileName = saveName;
                _loadedProfileConfig = config;
                OnPropertyChanged(nameof(HasUnsavedChanges));
            }
        }

        SelectedInstance = instance;
        if (instance != null)
        {
            LoadConfigToUI(instance.Configuration);
            _loadedProfileName = instance.ProfileName;
            _loadedProfileConfig = instance.Configuration;
            SetSelectedProfileSilently(instance.ProfileName);
            OnPropertyChanged(nameof(HasUnsavedChanges));
            OnPropertyChanged(nameof(WindowTitleWithProfile));
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

    private void ShowServerStartErrorAnimation()
    {
        DismissServerStartError();
        ShowServerStartError = true;
        _errorAnimationCts = new CancellationTokenSource();
        var cts = _errorAnimationCts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_errorAnimationCts == cts)
                        ShowServerStartError = false;
                });
            }
            catch (TaskCanceledException)
            {
            }
        });
    }

    public void DismissServerStartError()
    {
        _errorAnimationCts?.Cancel();
        _errorAnimationCts = null;
        ShowServerStartError = false;
        _selectedInstance?.DismissError();
    }

    private async Task StopServerAsync()
    {
        if (_selectedInstance != null)
            await _selectedInstance.StopAsync();
    }

    private async Task RestartServerAsync()
    {
        if (_selectedInstance != null)
        {
            var config = GetCurrentConfig();
            if (!config.RunInDocker && string.IsNullOrEmpty(config.ExecutablePath))
            {
                var defaultPath = _downloadService.GetDefaultLlamaServerPath();
                if (defaultPath != null)
                    config.ExecutablePath = defaultPath;
            }
            _selectedInstance.UpdateConfiguration(config);
            await _selectedInstance.RestartAsync(_supportedFlags, _validSpecTypeValues, _validCacheTypeValues);
        }
    }

    public async Task StopServerIfRunningAsync()
    {
        await StopAllInstancesAsync();
    }

    private async Task UnloadModelAsync()
    {
        if (_selectedInstance != null)
            await _selectedInstance.UnloadModelAsync();
    }

    public async Task<bool> LaunchInstanceAsync(string profileName, ServerConfiguration config)
    {
        try
        {
            var existingRunning = RunningInstances.FirstOrDefault(i => i.ProfileName == profileName && i.IsRunning);
            if (existingRunning != null)
            {
                SelectedInstance = existingRunning;
                return false;
            }

            // A previous failed attempt may still be in the panel as an error button.
            // Drop it so we can launch a fresh instance.
            var existingFailed = RunningInstances.FirstOrDefault(i => i.ProfileName == profileName);
            if (existingFailed != null)
                RemoveInstance(existingFailed);

            if (!config.RunInDocker && string.IsNullOrEmpty(config.ExecutablePath))
            {
                var defaultPath = _downloadService.GetDefaultLlamaServerPath();
                if (defaultPath != null)
                    config.ExecutablePath = defaultPath;
            }

            var targetHost = string.IsNullOrWhiteSpace(config.Host) ? "127.0.0.1" : config.Host;
            var targetPort = config.Port;
            var collision = RunningInstances.FirstOrDefault(i => i.IsRunning
                && i.Configuration.Host == targetHost
                && i.Configuration.Port == targetPort);
            if (collision != null)
            {
                var toastText = string.Format(LocalizedStrings.GetString("PortCollisionToast"), targetHost, targetPort, collision.ProfileName);
                Toasts.Show(toastText);
                return false;
            }

            var exePath = config.ExecutablePath;
            if (string.IsNullOrEmpty(exePath))
                exePath = _downloadService.GetDefaultLlamaServerPath() ?? "";
            var resolved = LlamaServerService.ResolveExecutablePath(exePath);
            var version = !string.IsNullOrEmpty(resolved) && File.Exists(resolved)
                ? (await _downloadService.GetLocalVersionTagAsync(resolved) ?? "unknown")
                : (!string.IsNullOrEmpty(exePath) && File.Exists(exePath)
                    ? (await _downloadService.GetLocalVersionTagAsync(exePath) ?? "unknown")
                    : "unknown");
            var logPrefix = $"[{profileName} | {version} | ";

            var instance = new ServerInstance(
                profileName,
                config,
                logPrefix,
                _logService,
                _autoRestart,
                _logEnabled);

            if (config.RunInDocker)
                instance.SetDockerService(_dockerService);

            instance.ServerStateChanged += OnInstanceServerStateChanged;
            instance.RequestRemove += OnInstanceRequestRemove;
            instance.PropertyChanged += OnInstancePropertyChanged;

            await FreeComfyUiIfEnabledAsync();

            await instance.StartAsync(_supportedFlags, _validSpecTypeValues, _validCacheTypeValues);

            if (instance.IsRunning)
            {
                RunningInstances.Add(instance);
                SelectedInstance = instance;
                return true;
            }
            else
            {
                // Process didn't start. Keep the instance in the panel (red toggle button)
                // so the user can still click it to load the broken profile; handlers stay
                // attached so a later restart updates the same button in place.
                instance.StartFailed = true;
                RunningInstances.Add(instance);
                SelectedInstance = instance;

                var msg = string.Format(LocalizedStrings.GetString("ServerStartFailedToast"), profileName);
                Toasts.ShowError(msg);

                return false;
            }
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to start instance '{profileName}': {ex.Message}");
            return false;
        }
    }

    private async Task OpenInBrowserAsync()
    {
        if (_selectedInstance != null)
            await _selectedInstance.OpenInBrowserAsync();
    }

    private void LoadProfiles()
    {
        _suppressProfileAutoLoad = true;
        try
        {
            Profiles.Clear();
            _routingProfileNames.Clear();
            var profiles = _configService.GetAllProfiles();
            foreach (var profile in profiles)
            {
                Profiles.Add(profile.Name);
                if (profile.Configuration != null && !string.IsNullOrWhiteSpace(profile.Configuration.ModelsDir))
                    _routingProfileNames.Add(profile.Name);
            }
        }
        finally
        {
            _suppressProfileAutoLoad = false;
        }
    }

    private async Task LoadScenariosAsync()
    {
        Scenarios.Clear();
        var scenarios = await _configService.GetAllScenariosAsync();
        foreach (var scenario in scenarios)
        {
            Scenarios.Add(scenario.Name);
        }
    }

    private void UpdateSelectedScenarioAutoStart()
    {
        if (string.IsNullOrEmpty(_selectedScenario))
        {
            SelectedScenarioAutoStart = false;
            return;
        }

        _ = UpdateScenarioAutoStartFromDiskAsync();
    }

    private async Task UpdateScenarioAutoStartFromDiskAsync()
    {
        var name = _selectedScenario;
        var scenario = await _configService.LoadScenarioAsync(name);
        if (scenario != null && _selectedScenario == name)
        {
            SelectedScenarioAutoStart = scenario.AutoStart;
        }
    }

    private async Task RunScenarioAsync()
    {
        if (_isScenarioRunning) return;
        _isScenarioRunning = true;
        try
        {
        var scenarioName = _selectedScenario;
        if (string.IsNullOrEmpty(scenarioName)) return;

        var scenario = await _configService.LoadScenarioAsync(scenarioName);
        if (scenario == null) return;

        _logService.AppLog(string.Format(LocalizedStrings.GetString("ScenarioRunning"), scenarioName));

        int launched = 0;
        for (int i = 0; i < scenario.ProfileNames.Count; i++)
        {
            var profileName = scenario.ProfileNames[i];
            var config = await _configService.LoadProfileAsync(profileName);

            if (config == null)
            {
                _logService.Warning(string.Format(LocalizedStrings.GetString("ScenarioProfileNotFound"), profileName));
                continue;
            }

            if (!config.RunInDocker && string.IsNullOrEmpty(config.ExecutablePath))
            {
                var defaultPath = _downloadService.GetDefaultLlamaServerPath();
                if (defaultPath != null)
                    config.ExecutablePath = defaultPath;
            }

            if (RunningInstances.Any(inst => inst.ProfileName == profileName))
            {
                _logService.Warning($"Instance '{profileName}' already running, skipping");
                continue;
            }

            try
            {
                if (await LaunchInstanceAsync(profileName, config))
                    launched++;
            }
            catch (Exception ex)
            {
                _logService.Error($"Failed to start instance '{profileName}' from scenario: {ex.Message}");
            }

            if (i < scenario.ProfileNames.Count - 1 && scenario.IntervalSeconds > 0)
            {
                await Task.Delay(scenario.IntervalSeconds * 1000);
            }
        }

        _logService.AppLog(string.Format(LocalizedStrings.GetString("ScenarioCompleted"), scenarioName, launched));
        }
        finally
        {
            _isScenarioRunning = false;
        }
    }

    public async Task RunAutoStartScenarioAsync()
    {
        if (!_scenariosEnabled) return;

        var scenarios = await _configService.GetAllScenariosAsync();
        var autoStartScenario = scenarios.FirstOrDefault(s => s.AutoStart);
        if (autoStartScenario == null) return;

        _logService.AppLog(string.Format(LocalizedStrings.GetString("ScenarioAutoStarted"), autoStartScenario.Name));
        SelectedScenario = autoStartScenario.Name;
        await RunScenarioAsync();
    }

    public async Task<Models.ScenarioInfo?> OpenScenarioDialogAsync(Models.ScenarioInfo? existing = null)
    {
        var allProfileInfos = _configService.GetAllProfiles();
        var allProfileNames = allProfileInfos.Select(p => p.Name).OrderBy(n => n).ToList();
        var profileConfigs = allProfileInfos.ToDictionary(p => p.Name, p => p.Configuration);
        var existingNames = new HashSet<string>(
            (await _configService.GetAllScenariosAsync()).Select(s => s.Name),
            StringComparer.OrdinalIgnoreCase);
        if (existing != null)
            existingNames.Remove(existing.Name);

        var vm = new ScenarioDialogViewModel(allProfileNames, profileConfigs, existingNames, existing,
            (name, config) => _configService.SaveProfileAsync(name, config));
        var dialog = new ScenarioDialogWindow();
        dialog.SetViewModel(vm, _configService, DialogGeometryDict);
        await dialog.ShowDialog(MainWindow.Instance!);

        if (dialog.CapturedGeometry != null) { DialogGeometryDict["ScenarioDialog"] = dialog.CapturedGeometry; await SaveSettingsAsync(); }

        if (vm.DialogResult)
            return vm.GetResult();
        return null;
    }

    private async Task EditScenarioAsync()
    {
        var scenarioName = _selectedScenario;
        if (string.IsNullOrEmpty(scenarioName)) return;

        var scenario = await _configService.LoadScenarioAsync(scenarioName);
        if (scenario == null) return;

        var result = await OpenScenarioDialogAsync(scenario);
        if (result == null) return;

        await _configService.SaveScenarioAsync(result);
        if (result.Name != scenarioName)
        {
            await _configService.DeleteScenarioAsync(scenarioName);
        }
        await LoadScenariosAsync();
        SelectedScenario = result.Name;
    }

    private async Task CreateScenarioAsync()
    {
        var result = await OpenScenarioDialogAsync(null);
        if (result == null) return;

        await _configService.SaveScenarioAsync(result);
        await LoadScenariosAsync();
        SelectedScenario = result.Name;
    }

    private async Task DeleteScenarioAsync()
    {
        var scenarioName = _selectedScenario;
        if (string.IsNullOrEmpty(scenarioName)) return;

        var result = await ShowConfirmAsync(
            string.Format(LocalizedStrings.GetString("ConfirmDeleteScenario"), scenarioName),
            LocalizedStrings.Instance.ConfirmTitle);

        if (result == MessageBoxResult.Yes)
        {
            await _configService.DeleteScenarioAsync(scenarioName);
            await LoadScenariosAsync();
            SelectedScenario = Scenarios.FirstOrDefault() ?? "";
        }
    }

    public async Task ToggleScenarioAutoStartAsync()
    {
        var scenarioName = _selectedScenario;
        if (string.IsNullOrEmpty(scenarioName)) return;

        var scenario = await _configService.LoadScenarioAsync(scenarioName);
        if (scenario == null) return;

        scenario.AutoStart = !scenario.AutoStart;

        if (scenario.AutoStart)
        {
            var allScenarios = await _configService.GetAllScenariosAsync();
            foreach (var s in allScenarios)
            {
                if (s.Name != scenarioName && s.AutoStart)
                {
                    s.AutoStart = false;
                    await _configService.SaveScenarioAsync(s);
                }
            }
        }

        await _configService.SaveScenarioAsync(scenario);
        SelectedScenarioAutoStart = scenario.AutoStart;
    }

    private async Task SaveProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(_loadedProfileName))
        {
            await SaveProfileAsAsync();
            return;
        }
        await SaveToProfileAsync(_loadedProfileName, GetCurrentConfig());
    }

    private async Task SaveProfileAsAsync()
    {
        var name = await ShowNameInputAsync(
            LocalizedStrings.Instance.SaveAs,
            LocalizedStrings.Instance.SaveAsPrompt,
            _loadedProfileName);
        name = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name)) return;
        await SaveToProfileAsync(name, GetCurrentConfig());
    }

    private async Task SaveToProfileAsync(string name, ServerConfiguration config)
    {
        config.ExecutablePath = NormalizeExecutablePathForSaving(config.ExecutablePath);
        await _configService.SaveProfileAsync(name, config);

        // Sync the running instance's cached config if it matches the saved profile
        var matchingInstance = RunningInstances.FirstOrDefault(i => i.ProfileName == name);
        if (matchingInstance != null)
        {
            var instanceConfig = GetCurrentConfig();
            if (!instanceConfig.RunInDocker && string.IsNullOrEmpty(instanceConfig.ExecutablePath))
            {
                var defaultPath = _downloadService.GetDefaultLlamaServerPath();
                if (defaultPath != null)
                    instanceConfig.ExecutablePath = defaultPath;
            }
            matchingInstance.UpdateConfiguration(instanceConfig);
        }

        LoadProfiles();
        _loadedProfileName = name;
        _loadedProfileConfig = config;
        SetSelectedProfileSilently(name);
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

    public async Task OnProfileSelectedAsync()
    {
        if (_suppressProfileAutoLoad) return;

        var name = SelectedProfile;
        if (string.IsNullOrWhiteSpace(name)) return;
        if (name == _loadedProfileName) return;

        if (HasUnsavedChanges)
        {
            var result = await ShowConfirmAsync(
                LocalizedStrings.Instance.UnsavedChangesMessage,
                LocalizedStrings.Instance.UnsavedChangesTitle);

            if (result == MessageBoxResult.Cancel)
            {
                SetSelectedProfileSilently(_loadedProfileName);
                return;
            }

            if (result == MessageBoxResult.Yes && !string.IsNullOrWhiteSpace(_loadedProfileName))
            {
                await SaveToProfileAsync(_loadedProfileName, GetCurrentConfig());
            }
        }

        var loadedConfig = await _configService.LoadProfileAsync(name);
        if (loadedConfig != null)
        {
            LoadConfigToUI(loadedConfig);
            _loadedProfileConfig = loadedConfig;
            _loadedProfileName = name;
            SetSelectedProfileSilently(name);
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
        var newName = await ShowNameInputAsync(
            LocalizedStrings.GetString("Rename"),
            LocalizedStrings.GetString("EnterNewProfileName"),
            oldName);
        if (string.IsNullOrWhiteSpace(newName) || newName == oldName) return;

        try
        {
            await _configService.RenameProfileAsync(oldName, newName);
            _loadedProfileName = newName;
            LoadProfiles();
            SetSelectedProfileSilently(newName);
            OnPropertyChanged(nameof(WindowTitleWithProfile));
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(string.Format(LocalizedStrings.GetString("RenameProfileFailed"), ex.Message));
        }
    }

    private async Task<string> ShowNameInputAsync(string title, string prompt, string initialValue)
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
            Text = prompt,
            Margin = new Avalonia.Thickness(5)
        });

        var textBox = new TextBox
        {
            Text = initialValue,
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

    public class CloneDialogResult
    {
        public string Name { get; set; } = "";
        public ServerConfiguration Config { get; set; } = new();
    }

    public static async Task<CloneDialogResult?> ShowCloneDialogAsync(
        string sourceName, ServerConfiguration sourceConfig, HashSet<string> existingNames, Window? owner = null)
    {
        var dialog = new Window
        {
            Title = string.Format(LocalizedStrings.GetString("CloneProfileTitle"), sourceName),
            Width = 400,
            Height = 350,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var panel = new StackPanel { Margin = new Avalonia.Thickness(15) };

        panel.Children.Add(new TextBlock
        {
            Text = LocalizedStrings.GetString("CloneProfileNameLabel"),
            Margin = new Avalonia.Thickness(0, 0, 0, 5)
        });

        var nameTextBox = new TextBox
        {
            Text = sourceName + " (copy)",
            Margin = new Avalonia.Thickness(0, 0, 0, 3),
            BorderBrush = Avalonia.Media.Brushes.Green,
            BorderThickness = new Avalonia.Thickness(1)
        };
        panel.Children.Add(nameTextBox);

        var warningText = new TextBlock
        {
            Foreground = Avalonia.Media.Brushes.Red,
            Margin = new Avalonia.Thickness(0, 0, 0, 5),
            IsVisible = false,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        panel.Children.Add(warningText);

        panel.Children.Add(new TextBlock
        {
            Text = LocalizedStrings.GetString("CloneProfileHost"),
            Margin = new Avalonia.Thickness(0, 5, 0, 2)
        });

        var hostTextBox = new TextBox
        {
            Text = sourceConfig.Host,
            Margin = new Avalonia.Thickness(0, 0, 0, 5)
        };
        panel.Children.Add(hostTextBox);

        var portLabel = new TextBlock
        {
            Text = LocalizedStrings.GetString("CloneProfilePort"),
            Margin = new Avalonia.Thickness(0, 5, 0, 2)
        };
        panel.Children.Add(portLabel);

        var portTextBox = new TextBox
        {
            Text = sourceConfig.Port.ToString(),
            Margin = new Avalonia.Thickness(0, 0, 0, 5)
        };
        panel.Children.Add(portTextBox);

        panel.Children.Add(new TextBlock
        {
            Text = LocalizedStrings.GetString("CloneProfileContextSize"),
            Margin = new Avalonia.Thickness(0, 5, 0, 2)
        });

        var contextTextBox = new TextBox
        {
            Text = sourceConfig.ContextSize?.ToString() ?? "",
            Margin = new Avalonia.Thickness(0, 0, 0, 10)
        };
        panel.Children.Add(contextTextBox);

        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Avalonia.Thickness(0, 5, 0, 0)
        };

        var okButton = new Button { Content = "OK", Margin = new Avalonia.Thickness(5), IsEnabled = true };
        var cancelButton = new Button { Content = LocalizedStrings.GetString("Cancel"), Margin = new Avalonia.Thickness(5) };

        bool ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                warningText.Text = LocalizedStrings.GetString("CloneProfileNameEmpty");
                warningText.IsVisible = true;
                nameTextBox.BorderBrush = Avalonia.Media.Brushes.Red;
                nameTextBox.BorderThickness = new Avalonia.Thickness(1);
                return false;
            }
            if (existingNames.Contains(name))
            {
                warningText.Text = LocalizedStrings.GetString("CloneProfileNameExists");
                warningText.IsVisible = true;
                nameTextBox.BorderBrush = Avalonia.Media.Brushes.Red;
                nameTextBox.BorderThickness = new Avalonia.Thickness(1);
                return false;
            }
            warningText.IsVisible = false;
            nameTextBox.BorderBrush = Avalonia.Media.Brushes.Green;
            nameTextBox.BorderThickness = new Avalonia.Thickness(1);
            return true;
        }

        ValidateName(nameTextBox.Text ?? "");

        nameTextBox.TextChanged += (_, _) =>
        {
            var name = nameTextBox.Text ?? "";
            var isValid = ValidateName(name);
            okButton.IsEnabled = isValid;
        };

        CloneDialogResult? result = null;

        okButton.Click += (s, e) =>
        {
            var name = (nameTextBox.Text ?? "").Trim();
            if (!ValidateName(name)) return;

            var clonedConfig = sourceConfig.Clone();
            var hostVal = hostTextBox.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(hostVal))
                clonedConfig.Host = hostVal;

            if (int.TryParse(portTextBox.Text?.Trim(), out var portVal))
                clonedConfig.Port = portVal;

            var ctxText = contextTextBox.Text?.Trim() ?? "";
            clonedConfig.ContextSize = string.IsNullOrEmpty(ctxText) ? null : (int.TryParse(ctxText, out var ctxVal) ? ctxVal : (int?)null);

            result = new CloneDialogResult { Name = name, Config = clonedConfig };
            dialog.Close();
        };

        cancelButton.Click += (s, e) => { dialog.Close(); };

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        panel.Children.Add(buttonPanel);

        dialog.Content = panel;

        await dialog.ShowDialog(owner ?? MainWindow.Instance!);
        return result;
    }

    private async Task CloneProfileAsync()
    {
        var sourceName = SelectedProfile;
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            await ShowWarningAsync(LocalizedStrings.GetString("PleaseEnterProfileName"));
            return;
        }

        var sourceConfig = await _configService.LoadProfileAsync(sourceName);
        if (sourceConfig == null)
        {
            await ShowWarningAsync(string.Format(LocalizedStrings.GetString("ErrorLoadingFile"), sourceName));
            return;
        }

        var existingNames = new HashSet<string>(Profiles, StringComparer.OrdinalIgnoreCase);
        var cloneResult = await ShowCloneDialogAsync(sourceName, sourceConfig, existingNames);
        if (cloneResult == null) return;

        await _configService.SaveProfileAsync(cloneResult.Name, cloneResult.Config);
        LoadProfiles();
        LoadConfigToUI(cloneResult.Config);
        _loadedProfileName = cloneResult.Name;
        _loadedProfileConfig = cloneResult.Config;
        SetSelectedProfileSilently(cloneResult.Name);
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(WindowTitleWithProfile));
    }

    private async Task ExportProfileAsync()
    {
        var filePath = await WindowsFileDialogs.SaveFileDialogAsync(
            LocalizedStrings.Instance.ExportDialogTitle,
            "json",
            new[] {
                ("JSON profile (*.json)", new[] { "*.json" }),
                ("Windows batch (*.bat)", new[] { "*.bat" }),
                ("MacOS script (*.command)", new[] { "*.command" }),
                ("Linux script (*.sh)", new[] { "*.sh" })
            });
        
        if (!string.IsNullOrEmpty(filePath))
        {
            var config = GetCurrentConfig();
            
            if (filePath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            {
                var command = CommandLineBuilder.BuildFullCommand(config, _supportedFlags, _validSpecTypeValues, _validCacheTypeValues);
                var batContent = $"@echo off\n{command}\npause";
                await System.IO.File.WriteAllTextAsync(filePath, batContent);
                _logService.Info($"Profile exported to BAT '{filePath}'");
            }
            else if (filePath.EndsWith(".command", StringComparison.OrdinalIgnoreCase))
            {
                var command = CommandLineBuilder.BuildFullCommand(config, _supportedFlags, _validSpecTypeValues, _validCacheTypeValues);
                var commandContent = $"#!/bin/bash\n{command}\necho 'Press Enter to exit...'\nread";
                await System.IO.File.WriteAllTextAsync(filePath, commandContent);
                _logService.Info($"Profile exported to MacOS command script '{filePath}'");
            }
            else if (filePath.EndsWith(".sh", StringComparison.OrdinalIgnoreCase))
            {
                var command = CommandLineBuilder.BuildFullCommand(config, _supportedFlags, _validSpecTypeValues, _validCacheTypeValues);
                var shContent = $"#!/bin/bash\n{command}\necho 'Press Enter to exit...'\nread";
                await System.IO.File.WriteAllTextAsync(filePath, shContent);
                _logService.Info($"Profile exported to Linux shell script '{filePath}'");
            }
            else
            {
                config.ExecutablePath = NormalizeExecutablePathForSaving(config.ExecutablePath);
                await _configService.ExportProfileAsync(filePath, config);
            }
        }
    }

    private async Task ImportProfileAsync()
    {
        var result = await WindowsFileDialogs.OpenFileDialogAsync(
            "Import Profile",
            new[] { ("JSON files", new[] { "*.json" }) },
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
            new[] { (LocalizedStrings.Instance.ExportAllFilter, new[] { "*.zip" }) });

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
            new[] { ("Backup archive", new[] { "*.zip" }) });

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
        _shownConflictToasts.Clear();
        ExecutablePath = string.Empty;
        ModelPath = string.Empty;
        ModelsDir = string.Empty;
        Host = "127.0.0.1";
        Port = "8080";
        ContextSize = string.Empty;
        Threads = string.Empty;
        GpuLayers = string.Empty;
        CpuMoe = string.Empty;
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
        ParallelSlots = string.Empty;
        ContBatching = null;
        Timeout = string.Empty;
        CachePrompt = null;
        Mlock = null;
        Mmap = null;
        Reasoning = null;
        ReasoningBudget = string.Empty;
        Seed = string.Empty;
        PresencePenalty = string.Empty;
        FrequencyPenalty = string.Empty;
        ContextShift = null;

        LoadConfigToUI(config);
    }

    private async Task ExportToBatAsync()
    {
        var filePath = await WindowsFileDialogs.SaveFileDialogAsync(
            "Export to BAT",
            "bat",
            new[] { ("BAT files", new[] { "*.bat" }) });
        if (!string.IsNullOrEmpty(filePath))
        {
            var config = GetCurrentConfig();
            var command = CommandLineBuilder.BuildFullCommand(config, _supportedFlags, _validSpecTypeValues, _validCacheTypeValues);
            var batContent = $"@echo off\n{command}\npause";
            await System.IO.File.WriteAllTextAsync(filePath, batContent);
        }
    }

    private void ClearLog()
    {
        lock (_pendingLogs)
        {
            _pendingLogs.Clear();
        }
        LogLines.Clear();
        LogText = string.Empty;
    }

    private void CopyLog()
    {
    }

    private async Task SaveLogAsync()
    {
        _logService.Flush();
        string text;
        using (var fs = new System.IO.FileStream(
            _logService.LogFilePath,
            System.IO.FileMode.Open,
            System.IO.FileAccess.Read,
            System.IO.FileShare.ReadWrite))
        using (var reader = new System.IO.StreamReader(fs, System.Text.Encoding.UTF8))
        {
            text = await reader.ReadToEndAsync();
        }
        if (string.IsNullOrEmpty(text)) return;
        var filePath = await WindowsFileDialogs.SaveFileDialogAsync(
            Resources.LocalizedStrings.Instance.SaveLogDialogTitle,
            "log",
            new[] { ("Log files (*.log)", new[] { "*.log" }), ("Text files (*.txt)", new[] { "*.txt" }), ("All files", new[] { "*" }) });
        if (!string.IsNullOrEmpty(filePath))
        {
            await System.IO.File.WriteAllTextAsync(filePath, text);
            _logService.Info($"Log saved to '{filePath}'");
        }
    }

    private void OnLogReceived(object? sender, string logLine)
    {
        if (!LogEnabled) return;
        EnqueueLogLine(logLine);
    }

    private void EnqueueLogLine(string line)
    {
        lock (_pendingLogs)
        {
            _pendingLogs.Add(line);
            if (_logFlushScheduled) return;
            _logFlushScheduled = true;
        }
        Dispatcher.UIThread.Post(FlushLogs);
    }

    private void FlushLogs()
    {
        List<string> toFlush;
        lock (_pendingLogs)
        {
            toFlush = new List<string>(_pendingLogs);
            _pendingLogs.Clear();
            _logFlushScheduled = false;
        }

        foreach (var line in toFlush)
        {
            LogLines.Add(line);
            if (LogLines.Count > 1000)
            {
                LogLines.RemoveAt(0);
            }
        }

        if (toFlush.Count > 0)
        {
            LogText = string.Join("\n", LogLines);
        }
    }

    private async Task CheckDefaultLlamaVersionAsync()
    {
        try
        {
            var exePath = _downloadService.GetDefaultLlamaServerPath() ?? "";
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;

            var tag = await _downloadService.GetLocalVersionTagAsync(exePath);
            if (string.IsNullOrEmpty(tag)) return;

            if (_llamaCppInstalledTag != tag)
            {
                _llamaCppInstalledTag = tag;
                OnPropertyChanged(nameof(LlamaInstalledVersionTooltip));
                OnPropertyChanged(nameof(ShowLlamaUpdateButton));
                OnPropertyChanged(nameof(ShowLlamaDownloadButton));
                OnPropertyChanged(nameof(ShowLlamaChangeVersionButton));
                OnPropertyChanged(nameof(LlamaButtonText));
                await SaveSettingsAsync();
            }

            if (_cachedLlamaReleases.Count > 0)
            {
                var latestCachedTag = _cachedLlamaReleases[0].Tag;
                IsLlamaUpdateAvailable = latestCachedTag != _llamaCppInstalledTag;
            }
        }
        catch { }
    }

    private void StartPeriodicUpdateChecks()
    {
        _periodicCheckCts = new CancellationTokenSource();
        var ct = _periodicCheckCts.Token;
        _ = Task.Run(async () =>
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await TryRunUpdateChecksAsync();
            });

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), ct);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                if (ct.IsCancellationRequested) break;
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await TryRunUpdateChecksAsync();
                });
            }
        }, ct);
    }

    private async Task TryRunUpdateChecksAsync()
    {
        try
        {
            var now = DateTime.Now;
            if (now - _lastAppUpdateCheck >= TimeSpan.FromMinutes(_appUpdateCheckInterval))
                await CheckForAppUpdateAsync();
            if (now - _lastLlamaUpdateCheck >= TimeSpan.FromMinutes(_llamaUpdateCheckInterval))
                await CheckForLlamaUpdateAsync();
            if (_experimentalReposEnabled && now - _lastExperimentalUpdateCheck >= TimeSpan.FromMinutes(_experimentalUpdateCheckInterval))
                await CheckForExperimentalUpdatesAsync();
        }
        catch { }
    }

    private async Task CheckForLlamaUpdateAsync()
    {
        try
        {
            if (DateTime.Now - _lastLlamaUpdateCheck < TimeSpan.FromMinutes(_llamaUpdateCheckInterval)) return;
            _lastLlamaUpdateCheck = DateTime.Now;

            if (!_downloadService.IsLlamaCppInstalled()) return;

            _logService.Log(LogLevel.Info, $"Checking for llama.cpp updates on GitHub (interval: {_llamaUpdateCheckInterval} min)...");
            var releases = await _downloadService.GetLatestReleasesAsync(10);
            bool cacheChanged = releases.Count != _cachedLlamaReleases.Count
                || (releases.Count > 0 && _cachedLlamaReleases.Count > 0 && releases[0].Tag != _cachedLlamaReleases[0].Tag);

            _cachedLlamaReleases.Clear();
            foreach (var r in releases)
                _cachedLlamaReleases.Add(r);
            _cachedLlamaReleasesTimestamp = DateTime.Now;

            if (cacheChanged)
                await SaveSettingsAsync();

            if (_cachedLlamaReleases.Count > 0)
            {
                var release = _cachedLlamaReleases[0];
                if (release.Tag != _llamaCppInstalledTag)
                {
                    IsLlamaUpdateAvailable = true;
                    CacheReleaseBody(release.Tag, release.Body);
                    var cleaned = CleanReleaseBody(release.Body);
                    var desc = cleaned.Length > 300 ? cleaned[..300] + "..." : cleaned;
                    var currentVersionStr = string.Format(LocalizedStrings.GetString("CurrentVersionTooltip"), _llamaCppInstalledTag);
                    LlamaUpdateTooltip = $"{currentVersionStr}\n\n{release.Tag}\n{release.PublishedAt:yyyy-MM-dd HH:mm}\n{desc}";
                }
            }
        }
        catch { }
    }

    public void UpdateInstalledTag(string tag)
    {
        _llamaCppInstalledTag = tag;
        IsLlamaUpdateAvailable = false;
        OnPropertyChanged(nameof(CanStartServer));
        OnPropertyChanged(nameof(ShowLlamaUpdateButton));
        OnPropertyChanged(nameof(ShowLlamaDownloadButton));
        OnPropertyChanged(nameof(ShowLlamaChangeVersionButton));
        OnPropertyChanged(nameof(LlamaButtonText));
        OnPropertyChanged(nameof(LlamaInstalledVersionTooltip));
    }

    private static string CleanReleaseBody(string body)
    {
        var text = body;
        text = Regex.Replace(text, @"<details\s+open>\s*", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</details>", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<details[^>]*>.*?</details>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"\[([^\]]*)\]\([^)]*\)", "$1");
        text = Regex.Replace(text, @"\*\*([^*]*)\*\*", "$1");
        text = Regex.Replace(text, @"^\s*[\r\n]", "", RegexOptions.Multiline);
        text = text.Trim();
        return text;
    }

    private void CacheReleaseBody(string tag, string body)
    {
        if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(body)) return;

        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(body));

        if (_releaseBodyCache.ContainsKey(tag))
        {
            _releaseBodyCache[tag] = encoded;
            _releaseBodyCacheOrder.Remove(tag);
            _releaseBodyCacheOrder.Add(tag);
        }
        else
        {
            _releaseBodyCache[tag] = encoded;
            _releaseBodyCacheOrder.Add(tag);
        }

        while (_releaseBodyCacheOrder.Count > 20)
        {
            var oldest = _releaseBodyCacheOrder[0];
            _releaseBodyCacheOrder.RemoveAt(0);
            _releaseBodyCache.Remove(oldest);
        }
    }

    private async Task CheckForAppUpdateAsync()
    {
        try
        {
            if (DateTime.Now - _lastAppUpdateCheck < TimeSpan.FromMinutes(_appUpdateCheckInterval)) return;
            _lastAppUpdateCheck = DateTime.Now;

            _logService.Log(LogLevel.Info, $"Checking for application updates on GitHub (interval: {_appUpdateCheckInterval} min)...");
            var updateInfo = await _appUpdateService.CheckForUpdateAsync();
            if (updateInfo != null)
            {
                _pendingAppUpdate = updateInfo;
                _isAppUpdateAvailable = true;
                var desc = updateInfo.Body.Length > 200 ? updateInfo.Body[..200] + "..." : updateInfo.Body;
                AppUpdateTooltip = $"{updateInfo.Tag}\n{updateInfo.PublishedAt:yyyy-MM-dd HH:mm}\n{desc}";
                OnPropertyChanged(nameof(ShowAppUpdateButton));
            }
        }
        catch { }
    }

    private async Task CheckForExperimentalUpdatesAsync()
    {
        try
        {
            if (!_experimentalReposEnabled) return;
            if (DateTime.Now - _lastExperimentalUpdateCheck < TimeSpan.FromMinutes(_experimentalUpdateCheckInterval)) return;
            _lastExperimentalUpdateCheck = DateTime.Now;

            var enabledRepos = ExperimentalRepos.Where(r => r.Enabled).ToList();
            if (enabledRepos.Count == 0) return;

            _logService.Log(LogLevel.Info, $"Checking for experimental repo updates ({enabledRepos.Count} repos)...");

            foreach (var repo in enabledRepos)
            {
                try
                {
                    var releases = await _experimentalRepoService.FetchReleasesAsync(repo);
                    if (releases.Count == 0) continue;

                    var oldTopTag = repo.CachedReleases.Count > 0 ? repo.CachedReleases[0].Tag : "";
                    var newTopTag = releases[0].Tag;

                    repo.CachedReleases = releases;
                    repo.CachedReleasesTimestamp = DateTime.Now;

                    if (!string.IsNullOrEmpty(oldTopTag) && oldTopTag != newTopTag)
                    {
                        var name = !string.IsNullOrEmpty(repo.DisplayName) ? repo.DisplayName : repo.RepoUrl;
                        var msg = string.Format(LocalizedStrings.GetString("ExperimentalUpdateToast"), name, newTopTag);
                        Toasts.ShowNeutral(msg, 8000);
                    }
                }
                catch { }
            }

            await SaveSettingsAsync();
        }
        catch { }
    }

    public async Task UpdateAppAsync()
    {
        if (_pendingAppUpdate == null) return;

        try
        {
            if (string.IsNullOrEmpty(_pendingAppUpdate.Asset?.DownloadUrl))
            {
                var freshUpdate = await _appUpdateService.CheckForUpdateAsync();
                if (freshUpdate == null)
                {
                    _isAppUpdateAvailable = false;
                    OnPropertyChanged(nameof(ShowAppUpdateButton));
                    return;
                }
                _pendingAppUpdate = freshUpdate;
            }

            var confirm = await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var result = await MessageBox.ShowAsync(
                    MainWindow.Instance!,
                    LocalizedStrings.GetString("AppUpdateConfirm"),
                    LocalizedStrings.Instance.ConfirmTitle,
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);
                return result;
            });

            if (confirm != MessageBoxResult.Yes) return;

            ServerStatus = LocalizedStrings.GetString("AppUpdateDownloading");
            var cts = new System.Threading.CancellationTokenSource();
            var progress = new Progress<double>();

            var tempFile = await _appUpdateService.DownloadUpdateAsync(
                _pendingAppUpdate.Asset, progress, cts.Token);

            ServerStatus = LocalizedStrings.GetString("AppUpdateRestarting");

            await SaveSettingsAsync();
            await StopAllInstancesAsync();

            await Task.Delay(500);

            _appUpdateService.PerformUpdateAndRestart(tempFile);
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await MessageBox.ShowAsync(
                    MainWindow.Instance!,
                    string.Format(LocalizedStrings.GetString("AppUpdateFailed"), ex.Message),
                    LocalizedStrings.Instance.ErrorTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            });
        }
    }

    public async Task SaveSettingsAsync()
    {
        // Skip during init: setters fired while loading would capture not-yet-loaded
        // values (e.g. CustomBrowserPath) and overwrite app.json with them.
        if (_isInitializing) return;

        // If the existing file couldn't be read, don't replace it with the in-memory
        // defaults we're running on - leave the original file for next launch.
        if (_configService.LoadFailedKeepExisting) return;

        var settings = GetAppSettings();
        await _configService.SaveAppSettingsAsync(settings);
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        if (propertyName != null)
            DisableConflictingCustomArgs(propertyName);
    }

    private void DisableConflictingCustomArgs(string propertyName)
    {
        if (_isLoadingConfig || _isInitializing || _isUpdatingCustomArguments) return;
        if (CustomArgumentItems.Count == 0) return;

        var flags = ServerConfiguration.GetFlagsForProperty(propertyName);
        if (flags.Count == 0) return;

        // Only react to value (string) fields that currently hold a value.
        if (GetType().GetProperty(propertyName)?.GetValue(this) is not string value
            || string.IsNullOrWhiteSpace(value))
            return;

        Dispatcher.UIThread.Post(() =>
        {
            bool changed = false;
            foreach (var item in CustomArgumentItems)
            {
                if (item.IsEnabled && flags.Any(f => string.Equals(f, item.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    item.IsEnabled = false;
                    changed = true;
                }
            }

            if (changed)
                RebuildCustomArgumentsFromToggles();
        });
    }

    public void Dispose()
    {
        _periodicCheckCts?.Cancel();
        _periodicCheckCts?.Dispose();
        foreach (var instance in RunningInstances.ToList())
        {
            try { instance.Dispose(); }
            catch (Exception ex)
            {
                _logService?.Error($"Failed to dispose instance: {ex.Message}");
            }
        }
        RunningInstances.Clear();
        _logStreamService.Dispose();
        _logService?.Dispose();
        _serverService?.Dispose();
    }

    public ExperimentalRepoInfo? CreateExperimentalRepoFromDialog(string repoUrl, string displayName, string filterTags)
    {
        if (string.IsNullOrWhiteSpace(repoUrl)) return null;
        if (!ExperimentalRepoService.TryParseGitHubUrl(repoUrl, out _, out _)) return null;

        if (ExperimentalRepos.Any(r => r.RepoUrl.Equals(repoUrl.Trim(), StringComparison.OrdinalIgnoreCase)))
            return null;

        var repo = new Models.ExperimentalRepoInfo
        {
            RepoUrl = repoUrl.Trim(),
            DisplayName = displayName?.Trim() ?? "",
            FilterTags = string.IsNullOrWhiteSpace(filterTags) ? ExperimentalRepoService.GetDefaultFilterTags() : filterTags.Trim(),
            Enabled = true
        };

        if (string.IsNullOrEmpty(repo.DisplayName) && ExperimentalRepoService.TryParseGitHubUrl(repo.RepoUrl, out _, out var repoName))
            repo.DisplayName = repoName;

        ExperimentalRepos.Add(repo);
        _ = SaveSettingsAsync();
        return repo;
    }

    public bool UpdateExperimentalRepoFromDialog(Models.ExperimentalRepoInfo existing, string repoUrl, string displayName, string filterTags)
    {
        if (existing == null) return false;
        if (string.IsNullOrWhiteSpace(repoUrl)) return false;
        if (!ExperimentalRepoService.TryParseGitHubUrl(repoUrl, out _, out _)) return false;

        var duplicate = ExperimentalRepos.FirstOrDefault(r =>
            r.RepoUrl.Equals(repoUrl.Trim(), StringComparison.OrdinalIgnoreCase) && r != existing);
        if (duplicate != null) return false;

        existing.RepoUrl = repoUrl.Trim();
        existing.DisplayName = displayName?.Trim() ?? "";
        existing.FilterTags = string.IsNullOrWhiteSpace(filterTags) ? ExperimentalRepoService.GetDefaultFilterTags() : filterTags.Trim();

        if (string.IsNullOrEmpty(existing.DisplayName) && ExperimentalRepoService.TryParseGitHubUrl(existing.RepoUrl, out _, out var repoName))
            existing.DisplayName = repoName;

        var idx = ExperimentalRepos.IndexOf(existing);
        if (idx >= 0)
        {
            ExperimentalRepos[idx] = existing;
            OnPropertyChanged(nameof(ExperimentalRepos));
        }

        _ = SaveSettingsAsync();
        return true;
    }

    public void DeleteExperimentalRepo(Models.ExperimentalRepoInfo repo)
    {
        if (repo == null) return;
        ExperimentalRepos.Remove(repo);
        if (_selectedExperimentalRepo == repo)
            SelectedExperimentalRepo = null;
        _ = SaveSettingsAsync();
    }

    public void AddDefaultExperimentalRepos()
    {
        var defaults = ExperimentalRepoService.GetDefaultRepos();
        foreach (var def in defaults)
        {
            if (!ExperimentalRepos.Any(r => r.RepoUrl.Equals(def.RepoUrl, StringComparison.OrdinalIgnoreCase)))
            {
                ExperimentalRepos.Add(def);
            }
        }
        _ = SaveSettingsAsync();
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