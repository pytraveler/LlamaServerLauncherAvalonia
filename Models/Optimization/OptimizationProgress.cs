namespace LlamaServerLauncher.Models.Optimization;

public enum OptimizationPhase
{
    NglEstimation,
    Warmup,
    Stage1,
    Stage2,
    Stage3,
    ContextEstimation,
    Comparison,
    Done,
}

public sealed class OptimizationProgress
{
    public OptimizationPhase Phase { get; init; }
    public string Message { get; init; } = "";

    public int? Stage { get; init; }

    public int? TrialNumber { get; init; }

    public int? TrialsInStage { get; init; }

    public string? Command { get; init; }
    public double? Value { get; init; }
    public double? Stddev { get; init; }
    public double? PpValue { get; init; }

    public bool IsBest { get; init; }
}
