using System;
using System.Threading;
using System.Threading.Tasks;
using LlamaServerLauncher.Models.Optimization;

namespace LlamaServerLauncher.Services.Optimization;

public sealed class ContextEstimator
{
    private readonly LlamaBenchService _bench;
    private readonly LogService? _log;

    public ContextEstimator(LlamaBenchService bench, LogService? log = null)
    {
        _bench = bench;
        _log = log;
    }

    public async Task<int?> EstimateMaxAsync(
        BenchArgs probeTemplate,
        int minCtx = 512,
        int maxCtx = 131072,
        int step = 512,
        int timeoutSeconds = OptimizationConstants.ContextEstimationTimeoutSeconds,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        if (!_bench.Capabilities.SupportsCtxSize)
        {
            _log?.Warning("ContextEstimator: llama-bench build has no context flag; skipping (caller should fall back).");
            return null;
        }

        int lowK = minCtx / step;
        int highK = maxCtx / step;
        if (lowK < 1) lowK = 1;

        int best = 0;
        int low = lowK, high = highK;
        while (low <= high)
        {
            ct.ThrowIfCancellationRequested();
            int midK = (low + high) / 2;
            int ctx = midK * step;
            progress?.Report(ctx);
            _log?.Info($"ContextEstimator: probing context {ctx}");

            var args = probeTemplate with { CtxSize = ctx };
            var result = await _bench.RunRawAsync(_bench.BuildArgs(args), timeoutSeconds, ct);
            if (result.Success) { best = ctx; low = midK + 1; }
            else high = midK - 1;
        }

        if (best == 0)
        {
            _log?.Warning("ContextEstimator: no context size loaded successfully.");
            return null;
        }

        _log?.Info($"ContextEstimator: estimated max context = {best}");
        return best;
    }
}
