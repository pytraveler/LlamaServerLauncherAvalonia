using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LlamaServerLauncher.ViewModels;

public readonly record struct ProxyUpstream(string Host, int Port);

public interface IOnDemandProxyHost
{
    IReadOnlyList<string> GetProfileNames();

    string? GetFallbackProfileName();

    Task<ProxyUpstream?> EnsureProfileRunningAsync(string profileName, CancellationToken ct);

    Task StopActiveAsync();
}
