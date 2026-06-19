namespace LlamaServerLauncher.Models.Optimization;

public sealed class OptimizationResult
{
    public BenchMetric Metric { get; init; }

    public int Threads { get; init; }
    public int BatchSize { get; init; }
    public int UBatchSize { get; init; }

    public int? GpuLayers { get; init; }

    public int? NCpuMoe { get; init; }

    public bool? FlashAttn { get; init; }
    public string? OverrideKey { get; init; }
    public string? OverridePattern { get; init; }

    public double BestValue { get; init; }

    public int? RecommendedCtxSize { get; init; }

    public string OptimizedServerCmd { get; init; } = "";
    public string OptimizedBenchCmd { get; init; } = "";
    public string DefaultBenchCmd { get; init; } = "";

    public double? OptimizedBenchValue { get; init; }
    public double? DefaultBenchValue { get; init; }
    public double? ImprovementPct { get; init; }

    public bool ImprovedOverBaseline { get; init; } = true;
}
