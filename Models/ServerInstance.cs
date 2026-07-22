using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using LlamaServerLauncher.Services;

namespace LlamaServerLauncher.Models;

public class ServerInstance : INotifyPropertyChanged, IDisposable
{
    private readonly LlamaServerService _service;
    private readonly LogService _logService;
    private DockerCliService? _dockerService;
    private bool _autoRestart;
    private bool _logEnabled = true;
    private bool _isSelected;
    private bool _showServerStartError;
    private bool _startFailed;
    private DateTime? _serverStartTime;
    private CancellationTokenSource? _errorAnimationCts;
    private int _isAutoRestarting;
    private bool _disposed;
    private bool _isRestarting;
    private bool _isStarting;
    private bool _isReady;
    private bool _noMmapCrashSuspected;
    private CancellationTokenSource? _readinessCts;
    private CancellationTokenSource? _statsCts;
    private double? _promptTps;
    private double? _genTps;
    private int? _slotsBusy;
    private int? _slotsTotal;
    private int _statsVersion;

    private const int MaxRunLogLines = 8000;
    private readonly object _runLogLock = new();
    private readonly List<string> _runLog = new();

    public string ProfileName { get; }
    public ServerConfiguration Configuration { get; private set; }
    public string LogPrefix { get; }

    public void UpdateConfiguration(ServerConfiguration config) => Configuration = config;

    public LlamaServerService Service => _service;

    public bool IsRunning => _service.IsRunning;
    public bool IsBusy => _service.IsBusy;
    public bool IsReady => _isReady;
    public bool IsLoading => IsRunning && !_isReady;

    public bool IsStarting
    {
        get => _isStarting;
        private set
        {
            if (_isStarting != value)
            {
                _isStarting = value;
                OnPropertyChanged();
            }
        }
    }

    public bool NoMmapCrashSuspected
    {
        get => _noMmapCrashSuspected;
        private set
        {
            if (_noMmapCrashSuspected != value)
            {
                _noMmapCrashSuspected = value;
                OnPropertyChanged();
            }
        }
    }
    public double? PromptTps => _promptTps;
    public double? GenTps => _genTps;
    public int? SlotsBusy => _slotsBusy;
    public int? SlotsTotal => _slotsTotal;
    public int StatsVersion => _statsVersion;
    public int? ProcessId => _service.ProcessId;
    public string BaseUrl => _service.BaseUrl;
    public bool WasStoppedIntentionally => _service.WasStoppedIntentionally;
    public bool IsSingleModelMode => _service.IsSingleModelMode;

    public bool AutoRestart
    {
        get => _autoRestart;
        set { _autoRestart = value; OnPropertyChanged(); }
    }

    public bool LogEnabled
    {
        get => _logEnabled;
        set { _logEnabled = value; OnPropertyChanged(); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public bool ShowServerStartError
    {
        get => _showServerStartError;
        set
        {
            if (_showServerStartError != value)
            {
                _showServerStartError = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// True when the instance is shown as a failed (not running) server so the user can
    /// still click it to load its profile. The button is highlighted in the error color
    /// and its submenu actions are disabled. Stays set until restart or dismiss.
    /// </summary>
    public bool StartFailed
    {
        get => _startFailed;
        set
        {
            if (_startFailed != value)
            {
                _startFailed = value;
                OnPropertyChanged();
            }
        }
    }

    public event EventHandler<bool>? ServerStateChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action<ServerInstance>? RequestRemove;

    public ServerInstance(
        string profileName,
        ServerConfiguration configuration,
        string logPrefix,
        LogService logService,
        bool defaultAutoRestart,
        bool defaultLogEnabled)
    {
        ProfileName = profileName;
        Configuration = configuration;
        LogPrefix = logPrefix;
        _logService = logService;
        _autoRestart = defaultAutoRestart;
        _logEnabled = defaultLogEnabled;

        _service = new LlamaServerService(_logService);
        _service.LogPrefix = LogPrefix;
        _service.OutputReceived += OnServiceOutput;
        _service.ServerStateChanged += OnServiceStateChanged;
    }

    public void SetDockerService(DockerCliService dockerService)
    {
        _dockerService = dockerService;
    }

    public async Task StartAsync(HashSet<string>? supportedFlags = null,
        List<string>? validSpecTypeValues = null,
        List<string>? validCacheTypeValues = null)
    {
        IsStarting = true;
        try
        {
            if (PrepareGpuForLaunchAsync != null)
                await PrepareGpuForLaunchAsync();

            if (Configuration.RunInDocker && _dockerService != null)
                await _service.StartDockerAsync(_dockerService, Configuration, supportedFlags, validSpecTypeValues, validCacheTypeValues);
            else
                await _service.StartAsync(Configuration, supportedFlags, validSpecTypeValues, validCacheTypeValues);
        }
        finally
        {
            IsStarting = false;
        }
    }

    public async Task StopAsync()
    {
        await _service.StopAsync();
    }

    public async Task RestartAsync(HashSet<string>? supportedFlags = null,
        List<string>? validSpecTypeValues = null,
        List<string>? validCacheTypeValues = null)
    {
        _isRestarting = true;
        try
        {
            await StopAsync();
            if (_service.IsRunning)
            {
                _logService.Warning($"Instance '{ProfileName}' stop did not complete; aborting restart.");
                return;
            }
            await StartAsync(supportedFlags, validSpecTypeValues, validCacheTypeValues);
        }
        finally
        {
            _isRestarting = false;
        }
    }

    public async Task UnloadModelAsync()
    {
        await _service.UnloadModelAsync();
    }

    public async Task<List<string>> GetLoadedModelsAsync()
    {
        return await _service.GetLoadedModelsAsync();
    }

    public async Task UnloadSingleModelAsync(string modelId)
    {
        await _service.UnloadSingleModelAsync(modelId);
    }

    public static string? CustomBrowserPath { get; set; }

    public Func<Task>? PrepareGpuForLaunchAsync { get; set; }

    public Task OpenInBrowserAsync()
    {
        try
        {
            var url = _service.BaseUrl;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                _logService.Error($"Invalid server URL: {url}");
                return Task.CompletedTask;
            }
            var browserPath = CustomBrowserPath;
            if (!string.IsNullOrWhiteSpace(browserPath))
            {
                Process.Start(new ProcessStartInfo(browserPath, uri.ToString())
                {
                    UseShellExecute = false
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to open browser: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    public void DismissError()
    {
        _errorAnimationCts?.Cancel();
        _errorAnimationCts = null;
        ShowServerStartError = false;
    }

    private void OnServiceOutput(object? sender, string output)
    {
        if (InferenceStatsParser.TryParse(output) is { } stat)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (stat.Kind == InferenceStatKind.Prompt) SetPromptTps(stat.TokensPerSecond);
                else SetGenTps(stat.TokensPerSecond);
            });
        }

        if (!_noMmapCrashSuspected
            && ServerCrashAdvisor.ShouldSuggestDisableNoMmap(output, Configuration.Mmap == false))
        {
            Dispatcher.UIThread.Post(() => NoMmapCrashSuspected = true);
        }

        if (!ServerLogFilter.IsPollingNoise(output))
            AppendRunLog(output);

        if (!_logEnabled) return;
        if (ServerLogFilter.IsPollingNoise(output)) return;
        _logService.LogRaw($"{LogPrefix}[llama-server:{_service.ProcessId}] {output}");
    }

    private void AppendRunLog(string line)
    {
        lock (_runLogLock)
        {
            _runLog.Add(line);
            if (_runLog.Count > MaxRunLogLines)
                _runLog.RemoveRange(0, _runLog.Count - MaxRunLogLines);
        }
    }

    public string GetRunLogSnapshot()
    {
        lock (_runLogLock)
            return string.Join(Environment.NewLine, _runLog);
    }

    public void ClearRunLog()
    {
        lock (_runLogLock)
            _runLog.Clear();
    }

    private async void OnServiceStateChanged(object? sender, bool isRunning)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (!isRunning && _service.IsRunning)
                    return;

                if (isRunning)
                {
                    _serverStartTime = DateTime.Now;
                    DismissError();
                    StartFailed = false;
                    StartReadinessPoll();
                }
                else
                {
                    StopReadinessPoll();
                    if (_serverStartTime.HasValue &&
                        (DateTime.Now - _serverStartTime.Value).TotalSeconds < 5 &&
                        !_service.WasStoppedIntentionally)
                    {
                        ShowErrorAnimation();
                    }
                    _serverStartTime = null;
                }

                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(IsLoading));

                if (!isRunning && _service.WasStoppedIntentionally && !_isRestarting)
                {
                    RequestRemove?.Invoke(this);
                    ServerStateChanged?.Invoke(this, isRunning);
                    return;
                }

                if (!isRunning && _autoRestart && Interlocked.CompareExchange(ref _isAutoRestarting, 1, 0) == 0 && !_service.WasStoppedIntentionally)
                {
                    _logService.AppLog($"Instance '{ProfileName}' exited unexpectedly. Auto-restarting...");
                    await Task.Delay(1000);
                    try
                    {
                        await StartAsync();
                    }
                    catch (Exception ex)
                    {
                        _logService.Error($"Auto-restart failed for '{ProfileName}': {ex.Message}");
                        StartFailed = true;
                        ServerStateChanged?.Invoke(this, false);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _isAutoRestarting, 0);
                    }
                    return;
                }

                if (!isRunning && !_autoRestart && !_service.WasStoppedIntentionally)
                {
                    // Keep the failed server in the panel so the profile is still loadable.
                    StartFailed = true;
                }

                ServerStateChanged?.Invoke(this, isRunning);
            });
        }
        catch (TaskCanceledException) { }
    }

    private void StartReadinessPoll()
    {
        StopReadinessPoll();
        var cts = new CancellationTokenSource();
        _readinessCts = cts;
        var ct = cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (await _service.CheckHealthOnceAsync(ct))
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (!ct.IsCancellationRequested)
                                SetReady(true);
                        });
                        return;
                    }
                    await Task.Delay(500, ct);
                }
            }
            catch (OperationCanceledException) { }
        });
    }

    private void StopReadinessPoll()
    {
        _readinessCts?.Cancel();
        _readinessCts = null;
        SetReady(false);
    }

    private void SetReady(bool ready)
    {
        if (_isReady == ready) return;
        _isReady = ready;
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(IsLoading));
        if (ready) StartStatsPoll();
        else StopStatsPoll();
    }

    private void StartStatsPoll()
    {
        StopStatsPoll();
        var cts = new CancellationTokenSource();
        _statsCts = cts;
        var ct = cts.Token;
        _ = Task.Run(async () =>
        {
            var consecutiveNull = 0;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var json = await _service.GetSlotsStatusAsync(logErrors: false);
                    if (json == null)
                    {
                        if (++consecutiveNull >= 3) return;
                    }
                    else
                    {
                        consecutiveNull = 0;
                        var (total, busy) = ParseSlots(json);
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (!ct.IsCancellationRequested) SetSlots(total, busy);
                        });
                    }
                    await Task.Delay(2000, ct);
                }
            }
            catch (OperationCanceledException) { }
        });
    }

    private void StopStatsPoll()
    {
        _statsCts?.Cancel();
        _statsCts = null;
        SetPromptTps(null);
        SetGenTps(null);
        SetSlots(null, null);
    }

    private static (int? Total, int? Busy) ParseSlots(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return (null, null);
            var total = 0;
            var busy = 0;
            foreach (var slot in doc.RootElement.EnumerateArray())
            {
                total++;
                if (slot.TryGetProperty("is_processing", out var p) && p.ValueKind == JsonValueKind.True)
                    busy++;
                else if (slot.TryGetProperty("state", out var s) && s.ValueKind == JsonValueKind.Number
                         && s.TryGetInt32(out var sv) && sv != 0)
                    busy++;
            }
            return total > 0 ? (total, busy) : (null, null);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private void SetPromptTps(double? value)
    {
        if (_promptTps == value) return;
        _promptTps = value;
        BumpStats();
    }

    private void SetGenTps(double? value)
    {
        if (_genTps == value) return;
        _genTps = value;
        BumpStats();
    }

    private void SetSlots(int? total, int? busy)
    {
        if (_slotsTotal == total && _slotsBusy == busy) return;
        _slotsTotal = total;
        _slotsBusy = busy;
        BumpStats();
    }

    private void BumpStats()
    {
        _statsVersion++;
        OnPropertyChanged(nameof(StatsVersion));
    }

    private void ShowErrorAnimation()
    {
        DismissError();
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
            catch (TaskCanceledException) { }
        });
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _errorAnimationCts?.Cancel();
        _readinessCts?.Cancel();
        _statsCts?.Cancel();
        _service.OutputReceived -= OnServiceOutput;
        _service.ServerStateChanged -= OnServiceStateChanged;
        _service.Dispose();
        _disposed = true;
    }
}
