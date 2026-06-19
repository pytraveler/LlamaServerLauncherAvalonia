using System;
using System.Threading;
using System.Threading.Tasks;
using LlamaServerLauncher.Models.Optimization;

namespace LlamaServerLauncher.Services.Optimization;

public sealed class NglEstimator
{
    private readonly LlamaBenchService _bench;
    private readonly LogService? _log;

    public NglEstimator(LlamaBenchService bench, LogService? log = null)
    {
        _bench = bench;
        _log = log;
    }

    public async Task<int> EstimateMaxAsync(
        BenchArgs probeTemplate,
        int maxLayers = OptimizationConstants.DefaultMaxGpuLayers,
        int timeoutSeconds = OptimizationConstants.NglEstimationTimeoutSeconds,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        int low = 0, high = maxLayers;
        while (low < high)
        {
            ct.ThrowIfCancellationRequested();
            int mid = (low + high + 1) / 2;
            progress?.Report(mid);
            _log?.Info($"NglEstimator: probing -ngl {mid}");

            var args = probeTemplate with { GpuLayers = mid };
            var result = await _bench.RunRawAsync(_bench.BuildArgs(args), timeoutSeconds, ct);
            if (result.Success)
                low = mid;
            else
                high = mid - 1;
        }

        _log?.Info($"NglEstimator: estimated max ngl = {low}");
        return low;
    }
}
