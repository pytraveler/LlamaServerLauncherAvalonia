using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using LlamaServerLauncher.Models;

namespace LlamaServerLauncher.Services;

public class LlamaServerService : ILlamaServerService, IDisposable
{
    private Process? _process;
    private readonly LogService _logService;
    private DockerCliService? _dockerService;
    private ServerConfiguration? _currentConfig;
    private bool _disposed;
    private bool _isStoppingIntentionally;
    private bool _isBusy;
    private string? _dockerContainerName;

    public bool IsRunning => _process != null && !_process.HasExited;
    public bool IsSingleModelMode { get; private set; }
    public bool IsBusy => _isBusy || (IsRunning && _process != null && !_process.HasExited);
    public bool WasStoppedIntentionally => _isStoppingIntentionally;
    public int? ProcessId => _process?.Id;
    public string BaseUrl => _currentConfig != null
        ? $"http://{_currentConfig.Host}:{_currentConfig.Port}"
        : "http://127.0.0.1:8080";

    public event EventHandler<string>? OutputReceived;
    public event EventHandler<bool>? ServerStateChanged;

    public LlamaServerService(LogService logService)
    {
        _logService = logService;
    }

    public async Task StartAsync(ServerConfiguration config, HashSet<string>? supportedFlags = null, List<string>? validSpecTypeValues = null, List<string>? validCacheTypeValues = null)
    {
        if (_isBusy)
        {
            _logService.Warning("Server is busy (already starting)");
            return;
        }

        if (IsRunning)
        {
            _logService.Warning("Server is already running");
            return;
        }

        if (string.IsNullOrEmpty(config.ExecutablePath))
        {
            throw new InvalidOperationException("Executable path is not set. Download llama.cpp or specify the path manually.");
        }

        if (string.IsNullOrEmpty(config.ModelPath) && string.IsNullOrEmpty(config.ModelsDir) && string.IsNullOrEmpty(config.HfRepo) && string.IsNullOrEmpty(config.HfFile))
        {
            throw new InvalidOperationException("Model path, models directory, or Hugging Face repo/file must be specified");
        }

        _isBusy = true;
        _currentConfig = config;
        _isStoppingIntentionally = false;
        IsSingleModelMode = !string.IsNullOrEmpty(config.ModelPath) || !string.IsNullOrEmpty(config.HfRepo) || !string.IsNullOrEmpty(config.HfFile);

        var startInfo = new ProcessStartInfo
        {
            FileName = config.ExecutablePath,
            Arguments = CommandLineBuilder.Build(config, supportedFlags, validSpecTypeValues, validCacheTypeValues),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        _logService.Info($"Starting server: {config.ExecutablePath} {startInfo.Arguments}");

        try
        {
            _process = new Process { StartInfo = startInfo };
            _process.OutputDataReceived += OnOutputDataReceived;
            _process.ErrorDataReceived += OnErrorDataReceived;
            _process.EnableRaisingEvents = true;
            _process.Exited += OnProcessExited;

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            _logService.Info($"Server started with PID: {_process.Id}");
            ServerStateChanged?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to start server: {ex.Message}");
            throw;
        }
        finally
        {
            _isBusy = false;
        }
    }

    public async Task StartDockerAsync(DockerCliService dockerService, ServerConfiguration config, HashSet<string>? supportedFlags = null, List<string>? validSpecTypeValues = null, List<string>? validCacheTypeValues = null)
    {
        if (_isBusy)
        {
            _logService.Warning("Server is busy (already starting or pulling image)");
            return;
        }

        if (IsRunning)
        {
            _logService.Warning("Server is already running");
            return;
        }

        if (string.IsNullOrEmpty(config.ModelPath) && string.IsNullOrEmpty(config.ModelsDir) && string.IsNullOrEmpty(config.HfRepo) && string.IsNullOrEmpty(config.HfFile))
        {
            throw new InvalidOperationException("Model path, models directory, or Hugging Face repo/file must be specified");
        }

        _isBusy = true;
        _dockerService = dockerService;
        _currentConfig = config;
        _isStoppingIntentionally = false;
        IsSingleModelMode = !string.IsNullOrEmpty(config.ModelPath) || !string.IsNullOrEmpty(config.HfRepo) || !string.IsNullOrEmpty(config.HfFile);

        try
        {
            var imageExists = await dockerService.ImageExistsAsync(config.DockerImage);
            if (!imageExists)
            {
                _logService.Info($"Docker image '{config.DockerImage}' not found locally. Pulling...");
                try
                {
                    await dockerService.PullAsync(config.DockerImage);
                    _logService.Info($"Docker image '{config.DockerImage}' pulled successfully.");
                }
                catch (Exception ex)
                {
                    _logService.Error($"Failed to pull Docker image '{config.DockerImage}': {ex.Message}");
                    throw;
                }
            }

            var dockerCommand = CommandLineBuilder.BuildDockerCommand(config, supportedFlags, validSpecTypeValues, validCacheTypeValues);
            var dockerArgs = dockerCommand.Substring("docker ".Length);

            // Ensure container has a name for proper stop/kill
            var containerName = !string.IsNullOrWhiteSpace(config.DockerContainerName)
                ? config.DockerContainerName
                : $"llama-launcher-{Guid.NewGuid().ToString("N")[..8]}";
            _dockerContainerName = containerName;

            if (!dockerArgs.Contains("--name"))
            {
                dockerArgs = $"run --name \"{containerName}\" " + dockerArgs.Substring("run ".Length);
            }

            _logService.Info($"Starting Docker server: docker {dockerArgs}");

            _process = dockerService.CreateDockerRunProcess(dockerArgs);
            _process.OutputDataReceived += OnOutputDataReceived;
            _process.ErrorDataReceived += OnErrorDataReceived;
            _process.EnableRaisingEvents = true;
            _process.Exited += OnProcessExited;

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            _logService.Info($"Docker server started with PID: {_process.Id}");
            ServerStateChanged?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to start Docker server: {ex.Message}");
            throw;
        }
        finally
        {
            _isBusy = false;
        }
    }

    public async Task StopAsync()
    {
        if (!IsRunning)
        {
            _logService.Warning("Server is not running");
            return;
        }

        _isStoppingIntentionally = true;

        try
        {
            _logService.Info("Stopping server...");

            if (_process != null && !_process.HasExited)
            {
                // If Docker mode, stop the container properly first
                if (!string.IsNullOrEmpty(_dockerContainerName) && _dockerService != null)
                {
                    try
                    {
                        _logService.Info($"Stopping Docker container '{_dockerContainerName}'...");
                        await _dockerService.StopAsync(_dockerContainerName);
                        _logService.Info($"Docker container '{_dockerContainerName}' stopped.");
                    }
                    catch (Exception ex)
                    {
                        _logService.Warning($"Failed to stop Docker container gracefully: {ex.Message}");
                    }
                }

                _process.Kill(entireProcessTree: true);

                try
                {
                    await Task.Run(() => _process.WaitForExit(3000));
                }
                catch (InvalidOperationException)
                {
                }
            }

            _logService.Info("Server stopped");
            ServerStateChanged?.Invoke(this, false);
        }
        catch (Exception ex)
        {
            _logService.Error($"Error stopping server: {ex.Message}");
        }
        finally
        {
            if (_process != null)
            {
                _process.Dispose();
                _process = null;
            }
            _dockerContainerName = null;
        }
    }

    public async Task UnloadModelAsync()
    {
        if (!IsRunning)
        {
            _logService.Warning("Cannot unload model: server is not running");
            return;
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            var modelsResponse = await client.GetAsync($"{BaseUrl}/v1/models");
            if (!modelsResponse.IsSuccessStatusCode)
            {
                _logService.Warning($"Failed to get models list: {modelsResponse.StatusCode}");
                return;
            }

            var json = await modelsResponse.Content.ReadAsStringAsync();
            var modelsData = System.Text.Json.JsonDocument.Parse(json);

            var loadedModels = new List<string>();

            if (modelsData.RootElement.TryGetProperty("data", out var dataArray))
            {
                foreach (var model in dataArray.EnumerateArray())
                {
                    if (model.TryGetProperty("status", out var status) &&
                        status.TryGetProperty("value", out var statusValue) &&
                        statusValue.GetString() == "loaded" &&
                        model.TryGetProperty("id", out var id))
                    {
                        var modelId = id.GetString();
                        if (!string.IsNullOrEmpty(modelId))
                        {
                            loadedModels.Add(modelId);
                        }
                    }
                }
            }

            if (loadedModels.Count == 0)
            {
                _logService.Info("No loaded models found to unload");
                return;
            }

            foreach (var modelId in loadedModels)
            {
                var unloadContent = new StringContent(
                    $"{{\"model\":\"{modelId}\"}}",
                    Encoding.UTF8,
                    "application/json");

                var response = await client.PostAsync($"{BaseUrl}/models/unload", unloadContent);
                if (response.IsSuccessStatusCode)
                {
                    _logService.Info($"Model '{modelId}' unloaded successfully");
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    _logService.Warning($"Failed to unload model '{modelId}': {response.StatusCode} - {errorBody}");
                }
            }
        }
        catch (Exception ex)
        {
            _logService.Error($"Error unloading model: {ex.Message}");
        }
    }

    public async Task<string?> GetCurrentModelAsync()
    {
        if (!IsRunning)
        {
            return null;
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var response = await client.GetAsync($"{BaseUrl}/v1/models");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return json;
            }
        }
        catch (Exception ex)
        {
            _logService.Error($"Error getting current model: {ex.Message}");
        }

        return null;
    }

    public async Task<string?> GetSlotsStatusAsync()
    {
        if (!IsRunning)
        {
            return null;
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var response = await client.GetAsync($"{BaseUrl}/slots");
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync();
            }
        }
        catch (Exception ex)
        {
            _logService.Error($"Error getting slots status: {ex.Message}");
        }

        return null;
    }

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Data))
        {
            _logService.LogRaw(e.Data);
            OutputReceived?.Invoke(this, e.Data);
        }
    }

    private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Data))
        {
            _logService.LogRaw(e.Data);
            OutputReceived?.Invoke(this, e.Data);
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        _logService.Info("Server process exited");
        _dockerContainerName = null;
        ServerStateChanged?.Invoke(this, false);
    }

    public void Dispose()
    {
        if (_disposed) return;

        if (IsRunning)
        {
            StopAsync().Wait();
        }

        _process?.Dispose();
        _disposed = true;
    }
}
