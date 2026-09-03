using System;

namespace LlamaServerLauncher.Services;

public enum VramFit
{
    Unknown,
    Fits,
    Tight,
    DoesNotFit
}

public sealed record VramRequest
{
    public int ContextSize { get; init; } = 4096;

    public int GpuLayers { get; init; } = -1;

    public string? CacheTypeK { get; init; }
    public string? CacheTypeV { get; init; }
    public int BatchSize { get; init; } = 2048;
    public int UBatchSize { get; init; } = 512;
    public bool FlashAttention { get; init; } = true;
    public int Parallel { get; init; } = 1;

    public int CpuMoeBlocks { get; init; }
}

public sealed record VramEstimate
{
    public long WeightBytes { get; init; }
    public long KvBytes { get; init; }
    public long ComputeBytes { get; init; }
    public long HostWeightBytes { get; init; }
    public long HostKvBytes { get; init; }
    public int OffloadedBlocks { get; init; }
    public int TotalBlocks { get; init; }
    public int KvBlocksOnGpu { get; init; }
    public bool FullyOffloaded { get; init; }

    public bool Approximate { get; init; }

    public long TotalBytes => WeightBytes + KvBytes + ComputeBytes;
    public long HostBytes => HostWeightBytes + HostKvBytes;
}

public static class VramEstimator
{
    public const long BackendOverheadBytes = 400L * 1024 * 1024;

    private const int ActivationTensors = 8;

    private const int KvPadding = 256;
    private const long MinSafetyBytes = 256L * 1024 * 1024;
    private const int SafetyPercent = 5;

    public static VramEstimate? Estimate(GgufModelInfo? info, VramRequest request)
    {
        if (info?.Tensors is not GgufTensorSummary tensors) return null;
        int blocks = tensors.BlockCount;
        if (blocks <= 0) return null;

        int headsFallback = info.HeadCountKv ?? info.HeadCount ?? 0;
        if (tensors.KvBlockCount > 0 && headsFallback <= 0 && info.HeadCountKvPerLayer == null) return null;

        int requested = request.GpuLayers < 0 ? blocks + 1 : request.GpuLayers;
        int offloaded = Math.Clamp(requested, 0, blocks);
        int firstGpuBlock = blocks - offloaded;
        bool full = requested > blocks;
        int cpuMoe = Math.Clamp(request.CpuMoeBlocks, 0, blocks);

        long weights = 0, hostWeights = 0;
        for (int i = 0; i < blocks; i++)
        {
            long block = tensors.BytesForBlock(i);
            long expertsOnHost = i < cpuMoe ? tensors.ExpertBytesForBlock(i) : 0;
            if (i >= firstGpuBlock)
            {
                weights += block - expertsOnHost;
                hostWeights += expertsOnHost;
            }
            else
            {
                hostWeights += block;
            }
        }

        long nonRepeating = Math.Max(0, tensors.TotalBytes - tensors.RepeatingBytes);
        if (full) weights += nonRepeating; else hostWeights += nonRepeating;

        bool approximate = false;
        long kvGpu = 0, kvHost = 0;
        int kvOnGpu = 0;
        for (int i = 0; i < blocks; i++)
        {
            if (!tensors.BlockHasKv(i)) continue;
            long bytes = KvBytesForLayer(info, request, i, ref approximate);
            if (i >= firstGpuBlock)
            {
                kvGpu += bytes;
                kvOnGpu++;
            }
            else
            {
                kvHost += bytes;
            }
        }

        long compute = offloaded > 0 ? ComputeBytes(info, request, full) : 0;

        return new VramEstimate
        {
            WeightBytes = weights,
            KvBytes = kvGpu,
            ComputeBytes = compute,
            HostWeightBytes = hostWeights,
            HostKvBytes = kvHost,
            OffloadedBlocks = offloaded,
            TotalBlocks = blocks,
            KvBlocksOnGpu = kvOnGpu,
            FullyOffloaded = full,
            Approximate = approximate,
        };
    }

    public static VramFit Judge(long totalBytes, long availableBytes)
    {
        if (availableBytes <= 0) return VramFit.Unknown;
        if (totalBytes + SafetyBytes(availableBytes) <= availableBytes) return VramFit.Fits;
        return totalBytes <= availableBytes ? VramFit.Tight : VramFit.DoesNotFit;
    }

    public static long SafetyBytes(long availableBytes) =>
        Math.Max(MinSafetyBytes, availableBytes / 100 * SafetyPercent);

    public static int MaxGpuLayers(GgufModelInfo? info, VramRequest request, long availableBytes)
    {
        if (info?.Tensors is not GgufTensorSummary tensors || availableBytes <= 0) return 0;
        int blocks = tensors.BlockCount;

        if (Fits(info, request with { GpuLayers = -1 }, availableBytes)) return -1;
        for (int layers = blocks; layers > 0; layers--)
            if (Fits(info, request with { GpuLayers = layers }, availableBytes))
                return layers;
        return 0;
    }

    public static int? MaxContext(GgufModelInfo? info, VramRequest request, long availableBytes)
    {
        if (info?.Tensors == null || availableBytes <= 0) return null;

        int limit = info.MaxContext ?? 1 << 20;
        int low = KvPadding, high = limit;
        if (!Fits(info, request with { ContextSize = low }, availableBytes)) return null;
        if (Fits(info, request with { ContextSize = high }, availableBytes)) return high;

        while (low + KvPadding <= high)
        {
            int mid = (low + high) / 2 / KvPadding * KvPadding;
            if (mid <= low) break;
            if (Fits(info, request with { ContextSize = mid }, availableBytes)) low = mid;
            else high = mid - KvPadding;
        }
        return low;
    }

    public static int? SuggestedCpuMoeBlocks(GgufModelInfo? info, VramRequest request, long availableBytes)
    {
        if (info?.Tensors is not GgufTensorSummary tensors || availableBytes <= 0) return null;
        if (tensors.ExpertBytes <= 0) return null;

        for (int blocks = 0; blocks <= tensors.BlockCount; blocks++)
            if (Fits(info, request with { CpuMoeBlocks = blocks }, availableBytes))
                return blocks;
        return null;
    }

    private static bool Fits(GgufModelInfo info, VramRequest request, long availableBytes) =>
        Estimate(info, request) is { } estimate && Judge(estimate.TotalBytes, availableBytes) == VramFit.Fits;

    private static long KvBytesForLayer(GgufModelInfo info, VramRequest request, int layer, ref bool approximate)
    {
        int heads = HeadsKv(info, layer);
        if (heads <= 0) return 0;

        bool swa = IsSlidingLayer(info, layer, ref approximate);
        int keyLength = swa ? info.KeyLengthSwa ?? KeyLength(info) : KeyLength(info);
        int valueLength = swa ? info.ValueLengthSwa ?? ValueLength(info) : ValueLength(info);
        if (keyLength <= 0 || valueLength <= 0) return 0;

        long context = swa ? SlidingContext(info, request) : PaddedContext(request.ContextSize);
        var (keyBlock, keySize) = CacheTypeSize(request.CacheTypeK);
        var (valueBlock, valueSize) = CacheTypeSize(request.CacheTypeV);

        long keys = context * heads * keyLength * keySize / keyBlock;
        long values = context * heads * valueLength * valueSize / valueBlock;
        return keys + values;
    }

    private static int HeadsKv(GgufModelInfo info, int layer)
    {
        if (info.HeadCountKvPerLayer is { } perLayer && layer < perLayer.Count && perLayer[layer] > 0)
            return perLayer[layer];
        return info.HeadCountKv ?? info.HeadCount ?? 0;
    }

    private static int KeyLength(GgufModelInfo info)
    {
        if (info.KeyLength is int explicitLength && explicitLength > 0) return explicitLength;
        if (info.EmbeddingLength is int embedding && info.HeadCount is int heads && heads > 0)
            return embedding / heads;
        return 0;
    }

    private static int ValueLength(GgufModelInfo info) =>
        info.ValueLength is int explicitLength && explicitLength > 0 ? explicitLength : KeyLength(info);

    private static bool IsSlidingLayer(GgufModelInfo info, int layer, ref bool approximate)
    {
        if (info.SlidingWindowPattern is { } pattern && layer < pattern.Count)
            return pattern[layer];

        if (info.SlidingWindow is not int window || window <= 0) return false;

        int period = SlidingPeriod(info.Architecture);
        if (period <= 1)
        {
            approximate = true;
            return false;
        }

        approximate = true;
        return layer % period < period - 1;
    }

    private static int SlidingPeriod(string? architecture) => architecture switch
    {
        "gemma2" => 2,
        "gemma3" => 6,
        "gemma3n" => 6,
        "cohere2" => 4,
        "gpt-oss" => 2,
        _ => 0
    };

    private static long SlidingContext(GgufModelInfo info, VramRequest request)
    {
        long window = info.SlidingWindow is int value && value > 0 ? value : request.ContextSize;
        long parallel = Math.Max(1, request.Parallel);
        long span = window * parallel + Math.Max(1, request.UBatchSize);
        return Math.Min(PaddedContext(request.ContextSize), Pad(span));
    }

    private static long PaddedContext(int contextSize) => Pad(Math.Max(1, contextSize));

    private static long Pad(long value) => (value + KvPadding - 1) / KvPadding * KvPadding;

    private static long ComputeBytes(GgufModelInfo info, VramRequest request, bool fullyOffloaded)
    {
        long ubatch = Math.Max(1, Math.Min(request.UBatchSize, Math.Max(1, request.ContextSize)));
        long embedding = info.EmbeddingLength ?? 4096;
        long total = BackendOverheadBytes;

        total += ubatch * embedding * 4 * ActivationTensors;

        long feedForward = info.ExpertFeedForwardLength ?? info.FeedForwardLength ?? embedding * 4;
        long experts = info.IsMoe ? Math.Max(1, info.ExpertUsedCount ?? 1) : 1;
        total += ubatch * feedForward * 4 * experts;

        if (!request.FlashAttention && info.HeadCount is int heads && heads > 0)
            total += heads * ubatch * PaddedContext(request.ContextSize) * 4;

        if (fullyOffloaded && info.VocabSize is int vocab && vocab > 0)
            total += (long)vocab * ubatch * 4;

        return total;
    }

    private static (long Block, long Size) CacheTypeSize(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return (1, 2);
        return type.Trim().ToLowerInvariant() switch
        {
            "f32" => (1, 4),
            "f16" => (1, 2),
            "bf16" => (1, 2),
            "q8_0" => (32, 34),
            "q5_0" => (32, 22),
            "q5_1" => (32, 24),
            "q4_0" => (32, 18),
            "q4_1" => (32, 20),
            "iq4_nl" => (32, 18),
            _ => (1, 2)
        };
    }
}
