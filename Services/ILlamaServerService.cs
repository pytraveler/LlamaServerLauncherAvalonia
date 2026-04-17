using System;
using System.Threading.Tasks;
using LlamaServerLauncher.Models;

namespace LlamaServerLauncher.Services;

public interface ILlamaServerService
{
    bool IsRunning { get; }
    bool IsSingleModelMode { get; }
    int? ProcessId { get; }
    string BaseUrl { get; }

    event EventHandler<string>? OutputReceived;
    event EventHandler<bool>? ServerStateChanged;

    Task StartAsync(ServerConfiguration config);
    Task StopAsync();
    Task UnloadModelAsync();
    Task<string?> GetCurrentModelAsync();
    Task<string?> GetSlotsStatusAsync();
}