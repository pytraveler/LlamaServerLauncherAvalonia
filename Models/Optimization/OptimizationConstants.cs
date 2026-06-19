namespace LlamaServerLauncher.Models.Optimization;

public static class OptimizationConstants
{
    public const int NglEstimationTimeoutSeconds = 620;
    public const int ContextEstimationTimeoutSeconds = 620;
    public const int BenchRunTimeoutSeconds = 820;

    public const int ServerHealthTimeoutSeconds = 300;

    public const int DefaultNTrials = 45;
    public const int DefaultRepeat = 3;
    public const int DefaultNTokens = 192;

    public const int DefaultWarmupRuns = 35;
    public const int MinWarmupRuns = 4;
    public const int DefaultWarmupTokens = 128;

    public const int DefaultMaxGpuLayers = 149;

    public const int DefaultMaxNCpuMoe = 64;

    public const int DefaultFitMarginMiB = 1024;
}
