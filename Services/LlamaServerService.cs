using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
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
    private DateTime? _processStartTime;
    private bool _serverStateChangedRaised;

    public string LogPrefix { get; set; } = "";

    public bool IsRunning
    {
        get
        {
            if (_process == null)
                return false;

            try
            {
                return !_process.HasExited;
            }
            catch (InvalidOperationException)
            {
                // Process object exists but was never started (e.g. Start() threw)
                return false;
            }
        }
    }

    public bool IsSingleModelMode { get; private set; }
    public bool IsBusy => _isBusy || IsRunning;
    public bool WasStoppedIntentionally => _isStoppingIntentionally;
    public int? ProcessId
    {
        get
        {
            if (_process == null)
                return null;

            try
            {
                return _process.Id;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }
    public string BaseUrl => _currentConfig != null
        ? $"http://{_currentConfig.Host}:{_currentConfig.Port}"
        : "http://127.0.0.1:8080";

    public event EventHandler<string>? OutputReceived;
    public event EventHandler<bool>? ServerStateChanged;

    public LlamaServerService(LogService logService)
    {
        _logService = logService;
    }

    public static string? ResolveExecutablePath(string executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName))
            return null;

        // If it's already an absolute or relative path with directory separators, check directly
        if (executableName.Contains(Path.DirectorySeparatorChar) || executableName.Contains(Path.AltDirectorySeparatorChar))
        {
            if (File.Exists(executableName) && IsExecutableFile(executableName))
                return Path.GetFullPath(executableName);
            return null;
        }

        var searchNames = new List<string> { executableName };

        if (OperatingSystem.IsWindows())
        {
            var hasExtension = Path.HasExtension(executableName);
            if (!hasExtension)
            {
                searchNames.Add(executableName + ".exe");
                searchNames.Add(executableName + ".cmd");
                searchNames.Add(executableName + ".bat");
            }
            else
            {
                var ext = Path.GetExtension(executableName).ToLowerInvariant();
                if (ext != ".exe")
                    searchNames.Add(Path.ChangeExtension(executableName, ".exe"));
                if (ext != ".cmd")
                    searchNames.Add(Path.ChangeExtension(executableName, ".cmd"));
                if (ext != ".bat")
                    searchNames.Add(Path.ChangeExtension(executableName, ".bat"));
            }
        }

        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var pathDirs = new HashSet<string>(comparer);

        void AddPaths(string? pathVar)
        {
            if (string.IsNullOrEmpty(pathVar)) return;
            foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!string.IsNullOrWhiteSpace(dir))
                    pathDirs.Add(dir.Trim());
            }
        }

        // Current process PATH
        AddPaths(Environment.GetEnvironmentVariable("PATH"));
        // User PATH (may have been updated during this session)
        try { AddPaths(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User)); } catch { }
        // Machine PATH
        try { AddPaths(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine)); } catch { }

        foreach (var dir in pathDirs)
        {
            foreach (var name in searchNames)
            {
                var fullPath = Path.Combine(dir, name);
                if (File.Exists(fullPath) && IsExecutableFile(fullPath))
                    return fullPath;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks whether the given file path points to an executable file.
    /// On Windows: any existing file is considered potentially executable (Process.Start validates further).
    /// On Linux/macOS: checks for execute permission bits.
    /// </summary>
    private static bool IsExecutableFile(string filePath)
    {
        if (OperatingSystem.IsWindows())
            return true;

        try
        {
            var mode = File.GetUnixFileMode(filePath);
            return (mode & UnixFileMode.UserExecute) != 0
                || (mode & UnixFileMode.GroupExecute) != 0
                || (mode & UnixFileMode.OtherExecute) != 0;
        }
        catch
        {
            // Fallback: if we can't check permissions, assume it's executable and let Process.Start fail later with a clearer error
            return true;
        }
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

        var resolvedExecutable = ResolveExecutablePath(config.ExecutablePath);
        if (resolvedExecutable == null)
        {
            throw new FileNotFoundException($"Executable not found: '{config.ExecutablePath}'. Ensure the file exists in PATH or provide a full path.");
        }

        if (string.IsNullOrEmpty(config.ModelPath) && string.IsNullOrEmpty(config.ModelsDir) && string.IsNullOrEmpty(config.HfRepo) && string.IsNullOrEmpty(config.HfFile))
        {
            throw new InvalidOperationException("Model path, models directory, or Hugging Face repo/file must be specified");
        }

        _isBusy = true;
        _currentConfig = config;
        _isStoppingIntentionally = false;
        _serverStateChangedRaised = false;
        IsSingleModelMode = !string.IsNullOrEmpty(config.ModelPath) || !string.IsNullOrEmpty(config.HfRepo) || !string.IsNullOrEmpty(config.HfFile);

        var startInfo = new ProcessStartInfo
        {
            FileName = resolvedExecutable,
            Arguments = CommandLineBuilder.Build(config, supportedFlags, validSpecTypeValues, validCacheTypeValues),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        _logService.Info($"Starting server: {resolvedExecutable} {startInfo.Arguments}");

        try
        {
            _process = new Process { StartInfo = startInfo };
            _process.OutputDataReceived += OnOutputDataReceived;
            _process.ErrorDataReceived += OnErrorDataReceived;
            _process.EnableRaisingEvents = true;
            _process.Exited += OnProcessExited;

            _process.Start();
            _processStartTime = DateTime.Now;
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            _logService.Info($"Server started with PID: {_process.Id}, executable: {resolvedExecutable}");
            ServerStateChanged?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to start server: {ex.Message}");
            if (_process != null)
            {
                try
                {
                    _process.OutputDataReceived -= OnOutputDataReceived;
                    _process.ErrorDataReceived -= OnErrorDataReceived;
                    _process.Exited -= OnProcessExited;
                    _process.Dispose();
                }
                catch { }
                _process = null;
            }
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
        _serverStateChangedRaised = false;
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
            if (_process != null)
            {
                try
                {
                    _process.OutputDataReceived -= OnOutputDataReceived;
                    _process.ErrorDataReceived -= OnErrorDataReceived;
                    _process.Exited -= OnProcessExited;
                    _process.Dispose();
                }
                catch { }
                _process = null;
            }
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
            // На macOS wrapper-скрипт мог выйти, но реальный сервер продолжает работать
            if (OperatingSystem.IsMacOS() && string.IsNullOrEmpty(_dockerContainerName) && _currentConfig != null && await IsPortListeningAsync())
            {
                _logService.Warning("Process object shows not running, but port is still listening. Possible wrapper script. Killing by port...");
                await KillProcessByPortAsync(_currentConfig.Port);
                _processStartTime = null;
                _dockerContainerName = null;
                if (!_serverStateChangedRaised)
                {
                    _serverStateChangedRaised = true;
                    ServerStateChanged?.Invoke(this, false);
                }
                return;
            }
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
            if (!_serverStateChangedRaised)
            {
                _serverStateChangedRaised = true;
                ServerStateChanged?.Invoke(this, false);
            }
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
            _processStartTime = null;
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
            OutputReceived?.Invoke(this, e.Data);
        }
    }

    private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Data))
        {
            OutputReceived?.Invoke(this, e.Data);
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_process == null) return;

        int? exitCode = null;
        try { exitCode = _process.ExitCode; } catch { }
        _logService.Info($"Server process exited. PID={_process.Id}, ExitCode={exitCode}, HasExited={_process.HasExited}");

        var lifetime = _processStartTime.HasValue ? (DateTime.Now - _processStartTime.Value).TotalSeconds : double.MaxValue;

        // На macOS процесс может быть wrapper-скриптом (например, shell-скрипт из Homebrew),
        // который форкает реальный сервер и сразу завершается. В этом случае .NET Process
        // отслеживает PID скрипта, а реальный llama-server продолжает работать.
        if (OperatingSystem.IsMacOS() && string.IsNullOrEmpty(_dockerContainerName) && lifetime < 5.0)
        {
            _logService.Warning($"Process exited after {lifetime:F1}s on macOS. Possible wrapper script. Checking port {_currentConfig?.Port}...");
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000); // дать время дочернему процессу открыть порт
                if (_isStoppingIntentionally)
                {
                    _logService.Info("Stop was intentional. Ignoring port check result.");
                    _processStartTime = null;
                    _dockerContainerName = null;
                    if (!_serverStateChangedRaised)
                    {
                        _serverStateChangedRaised = true;
                        ServerStateChanged?.Invoke(this, false);
                    }
                    return;
                }
                if (await IsPortListeningAsync())
                {
                    _logService.Info("Port is still listening. Real server is running. Re-raising ServerStateChanged(true).");
                    _serverStateChangedRaised = false; // reset so next stop can fire again
                    ServerStateChanged?.Invoke(this, true);
                    return;
                }
                _logService.Info("Port is not listening. Server is really stopped.");
                _processStartTime = null;
                _dockerContainerName = null;
                if (!_serverStateChangedRaised)
                {
                    _serverStateChangedRaised = true;
                    ServerStateChanged?.Invoke(this, false);
                }
            });
        }
        else
        {
            _processStartTime = null;
            _dockerContainerName = null;
            if (!_serverStateChangedRaised)
            {
                _serverStateChangedRaised = true;
                ServerStateChanged?.Invoke(this, false);
            }
        }
    }

    private async Task<bool> IsPortListeningAsync()
    {
        if (_currentConfig == null) return false;
        var host = _currentConfig.Host is "0.0.0.0" or "::" ? "127.0.0.1" : _currentConfig.Host;
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, _currentConfig.Port);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task KillProcessByPortAsync(int port)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "lsof",
                Arguments = $"-ti :{port}",
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            if (proc == null) return;
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            var pids = output.Split(new[] { '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var pidStr in pids)
            {
                if (int.TryParse(pidStr, out var pid))
                {
                    _logService.Info($"Killing process {pid} listening on port {port}");
                    try
                    {
                        Process.GetProcessById(pid).Kill(true);
                    }
                    catch (Exception ex)
                    {
                        _logService.Warning($"Failed to kill process {pid}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logService.Error($"Error killing process by port: {ex.Message}");
        }
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
