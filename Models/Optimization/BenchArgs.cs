namespace LlamaServerLauncher.Models.Optimization;

public sealed record BenchArgs
{
    public required string ModelPath { get; init; }

    public int? Threads { get; init; }
    public int? BatchSize { get; init; }
    public int? UBatchSize { get; init; }
    public int? GpuLayers { get; init; }

    public int? NCpuMoe { get; init; }

    public bool? FlashAttn { get; init; }

    public string? OverridePattern { get; init; }

    public int Repeat { get; init; } = OptimizationConstants.DefaultRepeat;

    public int NGen { get; init; }

    public int NPrompt { get; init; }

    public int? CtxSize { get; init; }

    public string? CacheTypeK { get; init; }

    public string? CacheTypeV { get; init; }

    public string? MmprojPath { get; init; }

    public bool UseFit { get; init; }

    public int FitMarginMiB { get; init; } = OptimizationConstants.DefaultFitMarginMiB;

    public bool NoWarmup { get; init; } = true;
}
