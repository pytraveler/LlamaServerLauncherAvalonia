using System;

namespace LlamaServerLauncher.Models.Optimization;

public sealed class SearchSpace
{
    public (int Low, int High) Batch { get; init; }
    public (int Low, int High) UBatch { get; init; }
    public (int Low, int High) Threads { get; init; }
    public (int Low, int High) GpuLayers { get; init; }

    public (int Low, int High) NCpuMoe { get; init; }

    public static SearchSpace Default() => new()
    {
        Batch = (256, 16384),
        UBatch = (256, 8192),
        Threads = (1, Math.Max(1, Environment.ProcessorCount)),
        GpuLayers = (0, OptimizationConstants.DefaultMaxGpuLayers),
        NCpuMoe = (0, 0),
    };

    public SearchSpace WithGpuLayersHigh(int high) => new()
    {
        Batch = Batch,
        UBatch = UBatch,
        Threads = Threads,
        GpuLayers = (GpuLayers.Low, high),
        NCpuMoe = NCpuMoe,
    };
}
