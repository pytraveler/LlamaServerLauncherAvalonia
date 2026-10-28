using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LlamaServerLauncher.Models;

namespace LlamaServerLauncher.Services;

public sealed record VramBudget
{
    public ServerConfiguration? Config { get; init; }
    public long AvailableBytes { get; init; }
    public long TotalBytes { get; init; }
    public ContextDefaults? Context { get; init; }
}

public sealed record ContextDefaults
{
    public static readonly ContextDefaults Current = new();

    public int UnsetContext { get; init; }
    public bool Fit { get; init; } = true;
}

public static class VramPlan
{
    public const int DefaultContext = 4096;
    public const int DefaultBatch = 2048;
    public const int DefaultUBatch = 512;
    public const int AutoParallelSlots = 4;
    public const int DefaultFitMinContext = 4096;
    public const int FitContextStep = 256;
    public const long DefaultFitTargetBytes = 1024L * 1024 * 1024;

    private static readonly string[] FitFlags = { "-fit", "--fit" };
    private static readonly string[] FitContextFlags = { "-fitc", "--fit-ctx" };
    private static readonly string[] FitTargetFlags = { "-fitt", "--fit-target" };
    private static readonly string[] UnifiedKvFlags = { "-kvu", "--kv-unified" };

    public static VramRequest RequestFrom(ServerConfiguration? config, int? modelMaxContext = null, long mmprojBytes = 0,
        ContextDefaults? defaults = null)
    {
        if (config == null) return new VramRequest { ContextSize = ChooseContext(null, modelMaxContext, defaults).Context };

        int batch = Positive(config.BatchSize) ?? DefaultBatch;
        int ubatch = Positive(config.UBatchSize) ?? DefaultUBatch;

        return new VramRequest
        {
            ContextSize = ChooseContext(config, modelMaxContext, defaults).Context,
            GpuLayers = ResolveGpuLayers(config.GpuLayers),
            CacheTypeK = config.CacheTypeK,
            CacheTypeV = config.CacheTypeV,
            BatchSize = batch,
            UBatchSize = Math.Min(ubatch, batch),
            FlashAttention = config.FlashAttention ?? true,
            Parallel = Positive(config.ParallelSlots) ?? 1,
            CpuMoeBlocks = Math.Max(0, config.CpuMoe ?? 0),
            MoeCacheBytes = Math.Max(0, config.MoeCacheMib ?? 0) * 1024L * 1024,
            CpuFfnBlocks = Math.Max(0, config.CpuFfn ?? 0),
            MmprojBytes = ProjectorBytes(config, mmprojBytes),
        };
    }

    public static (VramFit Verdict, long Bytes) Fit(GgufModelInfo? info, VramBudget? budget)
    {
        if (budget == null || (budget.AvailableBytes <= 0 && budget.TotalBytes <= 0))
            return (VramFit.Unknown, 0);

        var request = RequestFrom(budget.Config, info?.MaxContext, 0, budget.Context);
        var floor = ChooseContext(budget.Config, info?.MaxContext, budget.Context).FitFloor;
        var planned = FitContext(info, request, floor, budget.AvailableBytes, FitTargetBytes(budget.Config));
        var estimate = VramEstimator.Estimate(info, planned);
        if (estimate == null) return (VramFit.Unknown, 0);

        var verdict = FittedVerdict(planned.ContextSize, request.ContextSize, floor,
            estimate.TotalBytes, budget.AvailableBytes, budget.TotalBytes);
        return verdict == VramFit.Unknown ? (VramFit.Unknown, 0) : (verdict, estimate.TotalBytes);
    }

    public static VramFit Verdict(long needBytes, long availableBytes, long totalBytes)
    {
        if (needBytes <= 0) return VramFit.Unknown;
        if (availableBytes > 0) return VramEstimator.Judge(needBytes, availableBytes);
        return totalBytes > 0 ? VramFit.DoesNotFit : VramFit.Unknown;
    }

    public static long ProjectorBytes(ServerConfiguration? config, long mmprojBytes)
    {
        if (mmprojBytes <= 0 || config == null) return 0;
        if (string.IsNullOrWhiteSpace(config.MmprojPath)) return 0;
        return config.MmprojOffload == false ? 0 : mmprojBytes;
    }

    public static int ResolveGpuLayers(int? gpuLayers) => gpuLayers is int n && n >= 0 ? n : -1;

    public static int ResolveContext(int? contextSize, int? modelMaxContext, int? contextPerSlot = null, int? parallelSlots = null,
        ContextDefaults? defaults = null) =>
        ChooseContext(new ServerConfiguration
        {
            ContextSize = contextSize,
            ContextPerSlot = contextPerSlot,
            ParallelSlots = parallelSlots,
        }, modelMaxContext, defaults).Context;

    public static (int Context, int? FitFloor) ChooseContext(ServerConfiguration? config, int? modelMaxContext,
        ContextDefaults? defaults = null)
    {
        defaults ??= ContextDefaults.Current;
        int? contextSize = config?.ContextSize;

        if (contextSize is null && Positive(config?.ContextPerSlot) is int perSlot)
            return (Clamp((long)perSlot * (Positive(config?.ParallelSlots) ?? AutoParallelSlots)), null);

        int value = contextSize ?? defaults.UnsetContext;
        if (value > 0) return (value, null);
        if (Positive(modelMaxContext) is not int trained) return (DefaultContext, null);

        var custom = config == null ? null : CommandLineBuilder.ActiveCustomArguments(config);
        if (!FitEnabled(defaults, custom)) return (trained, null);

        long streams = SeparateStreams(config, custom);
        int full = Clamp(trained * streams);
        if (contextSize is not null) return (full, null);
        return (full, Clamp(Math.Min((long)FitMinContext(custom) * streams, full)));
    }

    public static VramFit FittedVerdict(int plannedContext, int fullContext, int? floor,
        long needBytes, long availableBytes, long totalBytes)
    {
        var verdict = Verdict(needBytes, availableBytes, totalBytes);
        bool shrunk = floor is int low && plannedContext > low && plannedContext < fullContext;
        return shrunk && verdict == VramFit.Tight ? VramFit.Fits : verdict;
    }

    public static VramRequest RunningRequest(ServerConfiguration? config, int? modelMaxContext, int serverContext,
        long mmprojBytes = 0, ContextDefaults? defaults = null)
    {
        var request = RequestFrom(config, modelMaxContext, mmprojBytes, defaults);
        return serverContext > 0 && ChooseContext(config, modelMaxContext, defaults).FitFloor != null
            ? request with { ContextSize = serverContext }
            : request;
    }

    public static long FitTargetBytes(ServerConfiguration? config)
    {
        var custom = config == null ? null : CommandLineBuilder.ActiveCustomArguments(config);
        if (custom != null)
            foreach (var flag in FitTargetFlags)
                if (custom.TryGetValue(flag, out var value) && value != null
                    && long.TryParse(value.Split(',')[0], NumberStyles.None, CultureInfo.InvariantCulture, out long mib))
                    return mib * 1024 * 1024;
        return DefaultFitTargetBytes;
    }

    public static VramRequest FitContext(GgufModelInfo? info, VramRequest request, int? floor, long availableBytes,
        long targetBytes = DefaultFitTargetBytes)
    {
        if (floor is not int low || low >= request.ContextSize || availableBytes <= 0) return request;
        long budget = availableBytes - Math.Max(0, targetBytes);
        if (WithinBudget(info, request, budget)) return request;

        var smallest = request with { ContextSize = low };
        if (!WithinBudget(info, smallest, budget)) return smallest;

        int fits = low, fails = request.ContextSize;
        while (fails - fits > FitContextStep)
        {
            int middle = fits + (fails - fits) / 2;
            if (WithinBudget(info, request with { ContextSize = middle }, budget)) fits = middle;
            else fails = middle;
        }
        return request with { ContextSize = Math.Max(low, fits - fits % FitContextStep) };
    }

    private static bool WithinBudget(GgufModelInfo? info, VramRequest request, long availableBytes) =>
        VramEstimator.Estimate(info, request) is { } estimate && estimate.TotalBytes <= availableBytes;

    private static bool FitEnabled(ContextDefaults defaults, IReadOnlyDictionary<string, string?>? custom)
    {
        if (!defaults.Fit) return false;
        if (custom == null) return true;
        foreach (var flag in FitFlags)
            if (custom.TryGetValue(flag, out var value) && value is "off" or "disabled" or "false" or "0")
                return false;
        return true;
    }

    private static int FitMinContext(IReadOnlyDictionary<string, string?>? custom)
    {
        if (custom != null)
            foreach (var flag in FitContextFlags)
                if (custom.TryGetValue(flag, out var value)
                    && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > 0)
                    return n;
        return DefaultFitMinContext;
    }

    private static long SeparateStreams(ServerConfiguration? config, IReadOnlyDictionary<string, string?>? custom)
    {
        if (Positive(config?.ParallelSlots) is not int slots) return 1;
        if (custom != null && UnifiedKvFlags.Any(custom.ContainsKey)) return 1;
        return slots;
    }

    private static int Clamp(long value) => (int)Math.Min(int.MaxValue, value);

    public static long FreeBytes(int? totalMb, int? usedMb)
    {
        if (totalMb is not int total || total <= 0) return 0;
        int used = usedMb is int u && u > 0 ? u : 0;
        if (used >= total) return 0;
        return (long)(total - used) * 1024 * 1024;
    }

    public static long TotalBytes(int? totalMb) =>
        totalMb is int total && total > 0 ? (long)total * 1024 * 1024 : 0;

    public const long MinSettleBytes = 256L * 1024 * 1024;
    public const int SettlePercent = 2;

    public static long Settle(long settled, long reading, long totalBytes)
    {
        if (reading <= 0 || settled <= 0) return reading;
        long threshold = Math.Max(MinSettleBytes, totalBytes / 100 * SettlePercent);
        return Math.Abs(reading - settled) >= threshold ? reading : settled;
    }

    public static long Available(long freeBytes, long reclaimBytes, long totalBytes)
    {
        long available = Math.Max(0, freeBytes) + Math.Max(0, reclaimBytes);
        return totalBytes > 0 ? Math.Min(available, totalBytes) : available;
    }

    public static double Gigabytes(long bytes) =>
        Math.Round(bytes / 1024.0 / 1024.0 / 1024.0, 1, MidpointRounding.AwayFromZero);

    private static int? Positive(int? value) => value is int v && v > 0 ? v : null;
}
