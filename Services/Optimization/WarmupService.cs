using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LlamaServerLauncher.Models.Optimization;

namespace LlamaServerLauncher.Services.Optimization;

public sealed class WarmupProgress
{
    public int Run { get; init; }
    public int TotalRuns { get; init; }
    public double TokensPerSec { get; init; }
}

public sealed class WarmupService
{
    private readonly LlamaBenchService _bench;
    private readonly LogService? _log;

    public WarmupService(LlamaBenchService bench, LogService? log = null)
    {
        _bench = bench;
        _log = log;
    }

    public async Task<IReadOnlyList<double>> WarmupAsync(
        BenchArgs template,
        int ngl,
        int? threads,
        BenchMetric metric,
        int nWarmupRuns = OptimizationConstants.DefaultWarmupRuns,
        int nWarmupTokens = OptimizationConstants.DefaultWarmupTokens,
        int timeoutSeconds = OptimizationConstants.BenchRunTimeoutSeconds,
        IProgress<WarmupProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (nWarmupRuns < OptimizationConstants.MinWarmupRuns)
            nWarmupRuns = OptimizationConstants.MinWarmupRuns;

        var args = template with
        {
            GpuLayers = ngl,
            Threads = threads,
            Repeat = 3,
            NGen = nWarmupTokens,
            NPrompt = nWarmupTokens,
        };

        var history = new List<double>(nWarmupRuns);
        for (int i = 0; i < nWarmupRuns; i++)
        {
            ct.ThrowIfCancellationRequested();
            var result = await _bench.RunBenchAsync(args, timeoutSeconds, ct);
            double value = result.MetricValue(metric);
            history.Add(value);
            _log?.Info($"Warmup {i + 1}/{nWarmupRuns}: {value:F2} tok/s");
            progress?.Report(new WarmupProgress { Run = i + 1, TotalRuns = nWarmupRuns, TokensPerSec = value });
        }

        return history;
    }
}
