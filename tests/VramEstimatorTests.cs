using System;
using System.Collections.Generic;
using LlamaServerLauncher.Services;

public static class VramEstimatorTests
{
    private const long MB = 1024 * 1024;
    private const long GB = 1024 * MB;

    private static GgufModelInfo Model(
        int blocks = 4,
        long blockBytes = GB,
        long expertBytes = 768 * MB,
        long nonRepeating = 512 * MB,
        bool kvEverywhere = true,
        IReadOnlyList<int>? headsPerLayer = null,
        IReadOnlyList<bool>? slidingPattern = null,
        int? slidingWindow = null,
        int? keyLengthSwa = null,
        int? maxContext = null,
        bool scalarHeads = true)
    {
        var blockList = new long[blocks];
        var expertList = new long[blocks];
        var kvList = new long[blocks];
        for (int i = 0; i < blocks; i++)
        {
            blockList[i] = blockBytes;
            expertList[i] = expertBytes;
            kvList[i] = kvEverywhere ? 1 : 0;
        }

        return new GgufModelInfo
        {
            Architecture = "test",
            BlockCount = blocks,
            MaxContext = maxContext,
            HeadCount = scalarHeads ? 8 : null,
            HeadCountKv = scalarHeads ? 8 : null,
            KeyLength = 128,
            ValueLength = 128,
            KeyLengthSwa = keyLengthSwa,
            ValueLengthSwa = keyLengthSwa,
            EmbeddingLength = 1024,
            VocabSize = 32000,
            SlidingWindow = slidingWindow,
            SlidingWindowPattern = slidingPattern,
            HeadCountKvPerLayer = headsPerLayer,
            Tensors = new GgufTensorSummary
            {
                TensorCount = blocks * 3,
                TotalBytes = blocks * blockBytes + nonRepeating,
                RepeatingBytes = blocks * blockBytes,
                ExpertBytes = blocks * expertBytes,
                EmbeddingBytes = nonRepeating / 2,
                OutputBytes = nonRepeating / 2,
                BlockBytes = blockList,
                BlockExpertBytes = expertList,
                BlockKvBytes = kvList,
            }
        };
    }

    private const long KvPerTokenPerLayer = 8L * 128 * 2 * 2;

    public static void Run(Harness h)
    {
        RunWeights(h);
        RunKvCache(h);
        RunVerdict(h);
        RunSuggestions(h);
    }

    private static void RunWeights(Harness h)
    {
        h.Section("VramEstimator: weights");

        var model = Model();
        var full = VramEstimator.Estimate(model, new VramRequest { ContextSize = 1024 });
        h.Check("offloading everything puts the whole file on the card",
            full?.WeightBytes == 4 * GB + 512 * MB, full?.WeightBytes.ToString() ?? "null");
        h.Check("nothing is left for system memory",
            full?.HostBytes == 0, full?.HostBytes.ToString() ?? "null");
        h.Check("the output head counts as offloaded too",
            full?.FullyOffloaded == true, (full?.FullyOffloaded)?.ToString() ?? "null");

        var half = VramEstimator.Estimate(model, new VramRequest { ContextSize = 1024, GpuLayers = 2 });
        h.Check("half the layers means half the blocks",
            half?.WeightBytes == 2 * GB, half?.WeightBytes.ToString() ?? "null");
        h.Check("embeddings and output stay in system memory until every layer is offloaded",
            half?.HostWeightBytes == 2 * GB + 512 * MB, half?.HostWeightBytes.ToString() ?? "null");
        h.Check("the blocks that moved are counted",
            half?.OffloadedBlocks == 2, half?.OffloadedBlocks.ToString() ?? "null");

        var none = VramEstimator.Estimate(model, new VramRequest { ContextSize = 1024, GpuLayers = 0 });
        h.Check("without offload the card holds nothing",
            none?.WeightBytes == 0 && none?.KvBytes == 0, none?.WeightBytes.ToString() ?? "null");
        h.Check("and no buffers are needed there either",
            none?.ComputeBytes == 0, none?.ComputeBytes.ToString() ?? "null");

        var moe = VramEstimator.Estimate(model, new VramRequest { ContextSize = 1024, CpuMoeBlocks = 2 });
        h.Check("experts sent to the host leave the card",
            moe?.WeightBytes == 4 * GB + 512 * MB - 2 * 768 * MB, moe?.WeightBytes.ToString() ?? "null");
        h.Check("and land in system memory",
            moe?.HostWeightBytes == 2 * 768 * MB, moe?.HostWeightBytes.ToString() ?? "null");
        h.Check("asking for more expert blocks than the model has is not an error",
            VramEstimator.Estimate(model, new VramRequest { CpuMoeBlocks = 99 })?.WeightBytes
                == 4 * GB + 512 * MB - 4 * 768 * MB, "clamped");

        h.Check("a model without a tensor table cannot be measured",
            VramEstimator.Estimate(new GgufModelInfo { BlockCount = 4 }, new VramRequest()) == null, "null");
        h.Check("nothing at all cannot be measured either",
            VramEstimator.Estimate(null, new VramRequest()) == null, "null");
    }

    private static void RunKvCache(Harness h)
    {
        h.Section("VramEstimator: kv cache");

        var model = Model();
        var at1k = VramEstimator.Estimate(model, new VramRequest { ContextSize = 1024 });
        h.Check("the cache covers every layer of the model",
            at1k?.KvBytes == 4 * 1024 * KvPerTokenPerLayer, at1k?.KvBytes.ToString() ?? "null");
        h.Check("all four layers are cached on the card",
            at1k?.KvBlocksOnGpu == 4, at1k?.KvBlocksOnGpu.ToString() ?? "null");

        var at2k = VramEstimator.Estimate(model, new VramRequest { ContextSize = 2048 });
        h.Check("twice the context is twice the cache",
            at2k?.KvBytes == 2 * at1k!.KvBytes, at2k?.KvBytes.ToString() ?? "null");

        var partial = VramEstimator.Estimate(model, new VramRequest { ContextSize = 1024, GpuLayers = 1 });
        h.Check("layers left on the host keep their cache there",
            partial?.KvBytes == 1024 * KvPerTokenPerLayer
            && partial?.HostKvBytes == 3 * 1024 * KvPerTokenPerLayer,
            $"{partial?.KvBytes}/{partial?.HostKvBytes}");

        var quantized = VramEstimator.Estimate(model,
            new VramRequest { ContextSize = 1024, CacheTypeK = "q8_0", CacheTypeV = "q8_0" });
        h.Check("a quantized cache is smaller by the block size of the type",
            quantized?.KvBytes == at1k!.KvBytes * 34 / 64, quantized?.KvBytes.ToString() ?? "null");

        var f32 = VramEstimator.Estimate(model,
            new VramRequest { ContextSize = 1024, CacheTypeK = "f32", CacheTypeV = "f32" });
        h.Check("a wider cache type is larger",
            f32?.KvBytes == at1k!.KvBytes * 2, f32?.KvBytes.ToString() ?? "null");

        var recurrent = Model(kvEverywhere: false);
        var noKv = VramEstimator.Estimate(recurrent, new VramRequest { ContextSize = 32768 });
        h.Check("blocks that keep state instead of a cache cost nothing per token",
            noKv?.KvBytes == 0, noKv?.KvBytes.ToString() ?? "null");
        h.Check("their weights are still measured",
            noKv?.WeightBytes == 4 * GB + 512 * MB, noKv?.WeightBytes.ToString() ?? "null");

        var noHeadsAnywhere = Model(headsPerLayer: new[] { 0, 0, 0, 0 }, scalarHeads: false);
        h.Check("a file that lists nothing but zero kv heads cannot be measured",
            VramEstimator.Estimate(noHeadsAnywhere, new VramRequest { ContextSize = 32768 }) == null, "null");

        var partialHeads = Model(headsPerLayer: new[] { 0, 8, 8, 8 }, scalarHeads: false);
        var partial2 = VramEstimator.Estimate(partialHeads, new VramRequest { ContextSize = 1024 });
        h.Check("a layer nobody can size is left out of the cache total",
            partial2?.KvBytes == 3 * 1024 * KvPerTokenPerLayer, partial2?.KvBytes.ToString() ?? "null");
        h.Check("and out of the count of cached layers",
            partial2?.KvBlocksOnGpu == 3, partial2?.KvBlocksOnGpu.ToString() ?? "null");
        h.Check("and the estimate says so",
            partial2?.Approximate == true, (partial2?.Approximate)?.ToString() ?? "null");

        var perLayer = Model(headsPerLayer: new[] { 4, 8, 8, 8 });
        var mixed = VramEstimator.Estimate(perLayer, new VramRequest { ContextSize = 1024 });
        h.Check("a layer with fewer kv heads caches less",
            mixed?.KvBytes == at1k!.KvBytes - 1024 * KvPerTokenPerLayer / 2,
            mixed?.KvBytes.ToString() ?? "null");

        var sliding = Model(
            slidingPattern: new[] { true, false, true, false },
            slidingWindow: 256,
            keyLengthSwa: 128);
        var swa = VramEstimator.Estimate(sliding, new VramRequest { ContextSize = 8192, UBatchSize = 512 });
        long fullLayers = 2 * 8192 * KvPerTokenPerLayer;
        long windowed = 2 * 768 * KvPerTokenPerLayer;   
        h.Check("a sliding window layer caches its window, not the whole context",
            swa?.KvBytes == fullLayers + windowed, swa?.KvBytes.ToString() ?? "null");
        h.Check("a declared layout is not a guess",
            swa?.Approximate == false, (swa?.Approximate)?.ToString() ?? "null");

        var guessed = Model(slidingWindow: 256) with { Architecture = "gemma3" };
        var byArch = VramEstimator.Estimate(guessed, new VramRequest { ContextSize = 8192 });
        h.Check("a known family gets its sliding layout from the launcher",
            byArch?.KvBytes < at1k!.KvBytes * 8 && byArch?.Approximate == true,
            byArch?.KvBytes.ToString() ?? "null");

        var unknown = Model(slidingWindow: 256) with { Architecture = "something-new" };
        var conservative = VramEstimator.Estimate(unknown, new VramRequest { ContextSize = 8192 });
        h.Check("an unknown family is measured as if every layer cached everything",
            conservative?.KvBytes == 4 * 8192 * KvPerTokenPerLayer && conservative?.Approximate == true,
            conservative?.KvBytes.ToString() ?? "null");
    }

    private static void RunVerdict(Harness h)
    {
        h.Section("VramEstimator: verdict");

        long card = 10 * GB;
        long margin = VramEstimator.SafetyBytes(card);
        h.Check("the margin is five percent of a big card",
            margin == card / 100 * 5, margin.ToString());
        h.Check("but never less than a quarter of a gigabyte",
            VramEstimator.SafetyBytes(1 * GB) == 256 * MB, VramEstimator.SafetyBytes(GB).ToString());

        h.Check("room to spare is a fit",
            VramEstimator.Judge(card - margin, card) == VramFit.Fits, "fits");
        h.Check("eating into the margin is tight",
            VramEstimator.Judge(card - margin + 1, card) == VramFit.Tight, "tight");
        h.Check("filling the card exactly is still tight",
            VramEstimator.Judge(card, card) == VramFit.Tight, "tight");
        h.Check("one byte over does not fit",
            VramEstimator.Judge(card + 1, card) == VramFit.DoesNotFit, "no");
        h.Check("without knowing the card there is no verdict",
            VramEstimator.Judge(GB, 0) == VramFit.Unknown, "unknown");
    }

    private static void RunSuggestions(Harness h)
    {
        h.Section("VramEstimator: suggestions");

        var model = Model();
        var request = new VramRequest { ContextSize = 1024 };

        h.Check("a card that swallows the model is offered all of it",
            VramEstimator.MaxGpuLayers(model, request, 32 * GB) == -1, "all");

        int layers = VramEstimator.MaxGpuLayers(model, request, 3 * GB);
        h.Check("a smaller card gets as many layers as its space allows",
            layers is > 0 and < 4, layers.ToString());
        var atLimit = VramEstimator.Estimate(model, request with { GpuLayers = layers });
        h.Check("the offer itself fits",
            VramEstimator.Judge(atLimit!.TotalBytes, 3 * GB) == VramFit.Fits, "fits");
        var oneMore = VramEstimator.Estimate(model, request with { GpuLayers = layers + 1 });
        h.Check("one layer more would not",
            VramEstimator.Judge(oneMore!.TotalBytes, 3 * GB) != VramFit.Fits, "does not");

        h.Check("a card that cannot hold a single layer is offered nothing",
            VramEstimator.MaxGpuLayers(model, request, 512 * MB) == 0, "none");

        var capped = Model(maxContext: 262144);
        h.Check("a model whose own limit fits is offered exactly that limit",
            VramEstimator.MaxContext(capped, request, 200 * GB) == 262144,
            VramEstimator.MaxContext(capped, request, 200 * GB)?.ToString() ?? "null");

        int? searched = VramEstimator.MaxContext(capped, request, 8 * GB);
        h.Check("a tighter card cuts the context below the model's limit",
            searched is > 256 and < 262144, searched?.ToString() ?? "null");
        h.Check("a searched offer is a whole number of cache pages",
            searched % 256 == 0, searched?.ToString() ?? "null");
        var atContext = VramEstimator.Estimate(capped, request with { ContextSize = searched ?? 0 });
        h.Check("the context offer fits",
            VramEstimator.Judge(atContext!.TotalBytes, 8 * GB) == VramFit.Fits, "fits");
        var onePageMore = VramEstimator.Estimate(capped, request with { ContextSize = (searched ?? 0) + 256 });
        h.Check("one cache page more would not",
            VramEstimator.Judge(onePageMore!.TotalBytes, 8 * GB) != VramFit.Fits, "does not");
        h.Check("a bigger card allows a longer context",
            VramEstimator.MaxContext(capped, request, 16 * GB) > searched,
            VramEstimator.MaxContext(capped, request, 16 * GB)?.ToString() ?? "null");

        h.Check("a model that declares an odd limit is not rounded down to please the search",
            VramEstimator.MaxContext(Model(maxContext: 200000), request, 200 * GB) == 200000,
            VramEstimator.MaxContext(Model(maxContext: 200000), request, 200 * GB)?.ToString() ?? "null");

        h.Check("a context beyond every reasonable file cannot break the search",
            VramEstimator.MaxContext(Model(maxContext: int.MaxValue), request, 40 * GB) > 256,
            VramEstimator.MaxContext(Model(maxContext: int.MaxValue), request, 40 * GB)?.ToString() ?? "null");

        h.Check("a card too small for the weights gets no context offer",
            VramEstimator.MaxContext(model, request, 2 * GB) == null, "null");

        int? experts = VramEstimator.SuggestedCpuMoeBlocks(model, request, 3 * GB);
        h.Check("moving experts to the host is offered by the block",
            experts is > 0 and <= 4, experts?.ToString() ?? "null");
        var withExperts = VramEstimator.Estimate(model, request with { CpuMoeBlocks = experts ?? 0 });
        h.Check("the offered number is the one that fits",
            VramEstimator.Judge(withExperts!.TotalBytes, 3 * GB) == VramFit.Fits, "fits");
        var oneBlockFewer = VramEstimator.Estimate(model, request with { CpuMoeBlocks = (experts ?? 1) - 1 });
        h.Check("keeping one more block of experts on the card would not fit",
            VramEstimator.Judge(oneBlockFewer!.TotalBytes, 3 * GB) != VramFit.Fits, "does not");
        h.Check("a card that fits the model needs no experts on the host",
            VramEstimator.SuggestedCpuMoeBlocks(model, request, 32 * GB) == 0, "none");
        h.Check("a model without experts has nothing to offer",
            VramEstimator.SuggestedCpuMoeBlocks(Model(expertBytes: 0), request, 3 * GB) == null, "null");
    }
}
