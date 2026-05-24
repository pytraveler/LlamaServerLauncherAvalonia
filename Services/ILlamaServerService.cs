using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LlamaServerLauncher.Models;

namespace LlamaServerLauncher.Services;

public interface ILlamaServerService
{
    bool IsRunning { get; }
    bool IsSingleModelMode { get; }
    bool IsBusy { get; }
    int? ProcessId { get; }
    string BaseUrl { get; }

    event EventHandler<string>? OutputReceived;
    event EventHandler<bool>? ServerStateChanged;

    Task StartAsync(ServerConfiguration config, HashSet<string>? supportedFlags = null, List<string>? validSpecTypeValues = null, List<string>? validCacheTypeValues = null);
    Task StopAsync();
    Task UnloadModelAsync();
    Task<string?> GetCurrentModelAsync();
    Task<string?> GetSlotsStatusAsync();
}