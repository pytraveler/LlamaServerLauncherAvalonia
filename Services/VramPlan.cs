using System;
using LlamaServerLauncher.Models;

namespace LlamaServerLauncher.Services;

public static class VramPlan
{
    public const int DefaultContext = 4096;
    public const int DefaultBatch = 2048;
    public const int DefaultUBatch = 512;

    public static VramRequest RequestFrom(ServerConfiguration? config, int? modelMaxContext = null)
    {
        if (config == null) return new VramRequest { ContextSize = ResolveContext(null, modelMaxContext) };

        int batch = Positive(config.BatchSize) ?? DefaultBatch;
        int ubatch = Positive(config.UBatchSize) ?? DefaultUBatch;

        return new VramRequest
        {
            ContextSize = ResolveContext(config.ContextSize, modelMaxContext),
            GpuLayers = ResolveGpuLayers(config.GpuLayers),
            CacheTypeK = config.CacheTypeK,
            CacheTypeV = config.CacheTypeV,
            BatchSize = batch,
            UBatchSize = Math.Min(ubatch, batch),
            FlashAttention = config.FlashAttention ?? true,
            Parallel = Positive(config.ParallelSlots) ?? 1,
            CpuMoeBlocks = Math.Max(0, config.CpuMoe ?? 0),
        };
    }

    public static int ResolveGpuLayers(int? gpuLayers) => gpuLayers is int n && n >= 0 ? n : -1;

    public static int ResolveContext(int? contextSize, int? modelMaxContext)
    {
        if (contextSize is not int value) return DefaultContext;
        if (value > 0) return value;
        return modelMaxContext is int trained && trained > 0 ? trained : DefaultContext;
    }

    public static long FreeBytes(int? totalMb, int? usedMb)
    {
        if (totalMb is not int total || total <= 0) return 0;
        int used = usedMb is int u && u > 0 ? u : 0;
        if (used >= total) return 0;
        return (long)(total - used) * 1024 * 1024;
    }

    public static long TotalBytes(int? totalMb) =>
        totalMb is int total && total > 0 ? (long)total * 1024 * 1024 : 0;

    public static double Gigabytes(long bytes) =>
        Math.Round(bytes / 1024.0 / 1024.0 / 1024.0, 1, MidpointRounding.AwayFromZero);

    private static int? Positive(int? value) => value is int v && v > 0 ? v : null;
}
