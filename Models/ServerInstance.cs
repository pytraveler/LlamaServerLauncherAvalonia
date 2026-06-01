using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
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
    private DateTime? _serverStartTime;
    private CancellationTokenSource? _errorAnimationCts;
    private int _isAutoRestarting;
    private bool _disposed;
    private bool _isRestarting;

    public string ProfileName { get; }
    public ServerConfiguration Configuration { get; private set; }
    public string LogPrefix { get; }

    public void UpdateConfiguration(ServerConfiguration config) => Configuration = config;

    public LlamaServerService Service => _service;

    public bool IsRunning => _service.IsRunning;
    public bool IsBusy => _service.IsBusy;
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
        if (Configuration.RunInDocker && _dockerService != null)
            await _service.StartDockerAsync(_dockerService, Configuration, supportedFlags, validSpecTypeValues, validCacheTypeValues);
        else
            await _service.StartAsync(Configuration, supportedFlags, validSpecTypeValues, validCacheTypeValues);
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
            Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
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
        if (!_logEnabled) return;
        _logService.LogRaw($"{LogPrefix}[llama-server:{_service.ProcessId}] {output}");
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
                }
                else
                {
                    if (_serverStartTime.HasValue &&
                        (DateTime.Now - _serverStartTime.Value).TotalSeconds < 5 &&
                        !_service.WasStoppedIntentionally)
                    {
                        ShowErrorAnimation();
                    }
                    _serverStartTime = null;
                }

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
                        RequestRemove?.Invoke(this);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _isAutoRestarting, 0);
                    }
                    return;
                }

                if (!isRunning && !_autoRestart && !_service.WasStoppedIntentionally)
                {
                    RequestRemove?.Invoke(this);
                }

                ServerStateChanged?.Invoke(this, isRunning);
            });
        }
        catch (TaskCanceledException) { }
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
        _service.OutputReceived -= OnServiceOutput;
        _service.ServerStateChanged -= OnServiceStateChanged;
        _service.Dispose();
        _disposed = true;
    }
}
