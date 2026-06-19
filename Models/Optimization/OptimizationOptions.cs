namespace LlamaServerLauncher.Models.Optimization;

public enum OverrideMode
{
    None,
    Scan,
}

public sealed class OptimizationOptions
{
    public required string ModelPath { get; init; }

    public string? LlamaBenchPath { get; init; }

    public string? LlamaServerExecutablePath { get; init; }

    public int? ContextSize { get; init; }

    public string? CacheTypeK { get; init; }

    public string? CacheTypeV { get; init; }

    public string? MmprojPath { get; init; }

    public BenchMetric Metric { get; init; } = BenchMetric.Mean;

    public int NTrials { get; init; } = OptimizationConstants.DefaultNTrials;

    public int Repeat { get; init; } = OptimizationConstants.DefaultRepeat;

    public int NTokens { get; init; } = OptimizationConstants.DefaultNTokens;

    public int NWarmupRuns { get; init; } = OptimizationConstants.DefaultWarmupRuns;
    public int NWarmupTokens { get; init; } = OptimizationConstants.DefaultWarmupTokens;
    public bool NoWarmup { get; init; }

    public OverrideMode OverrideMode { get; init; } = OverrideMode.None;

    public int? NglMax { get; init; }

    public bool UseFit { get; init; }

    public int FitMarginMiB { get; init; } = OptimizationConstants.DefaultFitMarginMiB;

    public SearchSpace SearchSpace { get; init; } = SearchSpace.Default();

    public bool UseHttpBenchmark { get; init; }

    public bool TuneNCpuMoe { get; init; }

    public bool NoMmap { get; init; }

    public bool PruneBatchLtUbatch { get; init; }

    public bool EstimateContext { get; init; }

    public int BenchTimeoutSeconds { get; init; } = OptimizationConstants.BenchRunTimeoutSeconds;
    public int NglTimeoutSeconds { get; init; } = OptimizationConstants.NglEstimationTimeoutSeconds;
}
