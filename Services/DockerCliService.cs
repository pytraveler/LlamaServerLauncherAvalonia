using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace LlamaServerLauncher.Services;

public class DockerCliService
{
    private readonly LogService _logService;
    private bool? _isInstalledCache;

    public DockerCliService(LogService logService)
    {
        _logService = logService;
    }

    public async Task<bool> IsDockerInstalledAsync()
    {
        if (_isInstalledCache.HasValue)
            return _isInstalledCache.Value;

        try
        {
            var (exitCode, output) = await RunDockerCommandAsync("--version", timeoutMs: 10000);
            _isInstalledCache = exitCode == 0;
            if (_isInstalledCache.Value)
                _logService.Info($"Docker detected: {output.Trim()}");
            else
                _logService.Warning("Docker not found (docker --version returned non-zero)");
            return _isInstalledCache.Value;
        }
        catch (Exception ex)
        {
            _logService.Warning($"Docker not available: {ex.Message}");
            _isInstalledCache = false;
            return false;
        }
    }

    public void InvalidateCache()
    {
        _isInstalledCache = null;
    }

    public async Task<bool> ImageExistsAsync(string image)
    {
        // Normalize image reference: if no tag, assume :latest
        var normalizedImage = image.Contains(':') ? image : $"{image}:latest";
        var (exitCode, output) = await RunDockerCommandAsync($"images --format \"{{{{.Repository}}}}:{{{{.Tag}}}}\" \"{normalizedImage}\"", timeoutMs: 30000);
        return exitCode == 0 && output.Trim().Contains(normalizedImage);
    }

    public async Task<string> PullAsync(string image)
    {
        var (exitCode, output) = await RunDockerCommandAsync($"pull \"{image}\"", timeoutMs: 600000);
        if (exitCode != 0)
            throw new InvalidOperationException($"docker pull failed: {output}");
        return output;
    }

    public async Task<string> PsAsync()
    {
        var (exitCode, output) = await RunDockerCommandAsync("ps -a", timeoutMs: 30000);
        return output;
    }

    public async Task<string> StartAsync(string containerName)
    {
        var (exitCode, output) = await RunDockerCommandAsync($"start \"{containerName}\"", timeoutMs: 30000);
        if (exitCode != 0)
            throw new InvalidOperationException($"docker start failed: {output}");
        return output;
    }

    public async Task<string> StopAsync(string containerName)
    {
        var (exitCode, output) = await RunDockerCommandAsync($"stop \"{containerName}\"", timeoutMs: 60000);
        if (exitCode != 0)
            throw new InvalidOperationException($"docker stop failed: {output}");
        return output;
    }

    public async Task<string> RmAsync(string containerName)
    {
        var (exitCode, output) = await RunDockerCommandAsync($"rm -f \"{containerName}\"", timeoutMs: 30000);
        if (exitCode != 0)
            throw new InvalidOperationException($"docker rm failed: {output}");
        return output;
    }

    public Process CreateDockerRunProcess(string arguments)
    {
        _logService.Info($"Docker run: docker {arguments}");
        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };
    }

    private async Task<(int exitCode, string output)> RunDockerCommandAsync(string args, int timeoutMs = 30000)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        var sb = new StringBuilder();
        process.OutputDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
        process.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await Task.Run(() => process.WaitForExit(timeoutMs));

        if (!process.HasExited)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return (-1, "Timeout");
        }

        return (process.ExitCode, sb.ToString());
    }
}
