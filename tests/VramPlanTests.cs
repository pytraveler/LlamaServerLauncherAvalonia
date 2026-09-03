using System;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Services;

public static class VramPlanTests
{
    private const long MB = 1024 * 1024;
    private const long GB = 1024 * MB;

    public static void Run(Harness h)
    {
        RunRequest(h);
        RunBudget(h);
        RunWiring(h);
        RunFit(h);
        RunVerdict(h);
    }

    private static void RunRequest(Harness h)
    {
        h.Section("VramPlan: form to request");

        var empty = VramPlan.RequestFrom(new ServerConfiguration());
        h.Check("an empty context field means the server default",
            empty.ContextSize == 4096, empty.ContextSize.ToString());
        h.Check("an empty layer field means everything is offloaded",
            empty.GpuLayers == -1, empty.GpuLayers.ToString());
        h.Check("an empty batch field means the server default",
            empty.BatchSize == 2048 && empty.UBatchSize == 512,
            $"{empty.BatchSize}/{empty.UBatchSize}");
        h.Check("flash attention is assumed on, as recent builds default to",
            empty.FlashAttention, empty.FlashAttention.ToString());
        h.Check("a single slot is assumed",
            empty.Parallel == 1, empty.Parallel.ToString());
        h.Check("no expert blocks are held back",
            empty.CpuMoeBlocks == 0, empty.CpuMoeBlocks.ToString());

        var missing = VramPlan.RequestFrom(null);
        h.Check("no configuration at all reads like an empty one",
            missing.ContextSize == 4096 && missing.GpuLayers == -1,
            $"{missing.ContextSize}/{missing.GpuLayers}");

        var filled = VramPlan.RequestFrom(new ServerConfiguration
        {
            ContextSize = 32768,
            GpuLayers = 20,
            CpuMoe = 6,
            BatchSize = 4096,
            UBatchSize = 1024,
            CacheTypeK = "q8_0",
            CacheTypeV = "q5_1",
            FlashAttention = false,
            ParallelSlots = 4,
        });
        h.Check("what the form says is what the estimate is asked for",
            filled.ContextSize == 32768 && filled.GpuLayers == 20 && filled.CpuMoeBlocks == 6,
            $"{filled.ContextSize}/{filled.GpuLayers}/{filled.CpuMoeBlocks}");
        h.Check("both cache types are carried over",
            filled.CacheTypeK == "q8_0" && filled.CacheTypeV == "q5_1",
            $"{filled.CacheTypeK}/{filled.CacheTypeV}");
        h.Check("flash attention turned off stays off",
            !filled.FlashAttention, filled.FlashAttention.ToString());
        h.Check("the slot count is carried over",
            filled.Parallel == 4, filled.Parallel.ToString());

        var wideUbatch = VramPlan.RequestFrom(new ServerConfiguration { BatchSize = 512, UBatchSize = 4096 });
        h.Check("a microbatch larger than the batch is clamped the way llama.cpp clamps it",
            wideUbatch.UBatchSize == 512, wideUbatch.UBatchSize.ToString());

        h.Check("zero layers means zero layers, not all of them",
            VramPlan.ResolveGpuLayers(0) == 0, VramPlan.ResolveGpuLayers(0).ToString());
        h.Check("a layer count that did not parse means all of them",
            VramPlan.ResolveGpuLayers(null) == -1, VramPlan.ResolveGpuLayers(null).ToString());
        h.Check("a negative layer count already means all of them",
            VramPlan.ResolveGpuLayers(-1) == -1, VramPlan.ResolveGpuLayers(-1).ToString());

        h.Check("-c 0 asks for the context the model was trained for",
            VramPlan.ResolveContext(0, 262144) == 262144, VramPlan.ResolveContext(0, 262144).ToString());
        h.Check("-c 0 falls back to the server default when the file does not say",
            VramPlan.ResolveContext(0, null) == 4096, VramPlan.ResolveContext(0, null).ToString());
        h.Check("a context of its own wins over what the model was trained for",
            VramPlan.ResolveContext(8192, 262144) == 8192, VramPlan.ResolveContext(8192, 262144).ToString());
        h.Check("a negative context is not a context",
            VramPlan.ResolveContext(-5, null) == 4096, VramPlan.ResolveContext(-5, null).ToString());

        var zeroBatch = VramPlan.RequestFrom(new ServerConfiguration { BatchSize = 0, UBatchSize = 0 });
        h.Check("a zero batch is no batch at all, so the defaults stand",
            zeroBatch.BatchSize == 2048 && zeroBatch.UBatchSize == 512,
            $"{zeroBatch.BatchSize}/{zeroBatch.UBatchSize}");

        var zeroSlots = VramPlan.RequestFrom(new ServerConfiguration { ParallelSlots = 0 });
        h.Check("zero slots still means one",
            zeroSlots.Parallel == 1, zeroSlots.Parallel.ToString());
    }

    private static void RunBudget(Harness h)
    {
        h.Section("VramPlan: what the card reports");

        h.Check("free memory is what the card is not already holding",
            VramPlan.FreeBytes(24576, 2048) == 22528L * MB,
            VramPlan.FreeBytes(24576, 2048).ToString());
        h.Check("a card that reports nothing gives no budget",
            VramPlan.FreeBytes(null, null) == 0, VramPlan.FreeBytes(null, null).ToString());
        h.Check("a total without a used figure counts as untouched",
            VramPlan.FreeBytes(8192, null) == 8192L * MB,
            VramPlan.FreeBytes(8192, null).ToString());
        h.Check("a card reported as full has nothing left",
            VramPlan.FreeBytes(8192, 8192) == 0, VramPlan.FreeBytes(8192, 8192).ToString());
        h.Check("a reading of more used than there is is not negative memory",
            VramPlan.FreeBytes(8192, 9000) == 0, VramPlan.FreeBytes(8192, 9000).ToString());

        h.Check("the total is the total",
            VramPlan.TotalBytes(24576) == 24576L * MB, VramPlan.TotalBytes(24576).ToString());
        h.Check("no total means no total",
            VramPlan.TotalBytes(null) == 0, VramPlan.TotalBytes(null).ToString());

        h.Check("gigabytes are counted in binary units",
            Math.Abs(VramPlan.Gigabytes(GB) - 1.0) < 1e-9, VramPlan.Gigabytes(GB).ToString());
        h.Check("and rounded to one decimal",
            Math.Abs(VramPlan.Gigabytes(GB + 512 * MB) - 1.5) < 1e-9,
            VramPlan.Gigabytes(GB + 512 * MB).ToString());
        h.Check("a midpoint rounds away from zero rather than to the even digit",
            Math.Abs(VramPlan.Gigabytes(GB / 4) - 0.3) < 1e-9,
            VramPlan.Gigabytes(GB / 4).ToString());
    }

    private static void RunWiring(Harness h)
    {
        h.Section("VramPlan: through to the estimate");

        var model = Model(blocks: 4, blockBytes: GB);

        var everything = VramEstimator.Estimate(model, VramPlan.RequestFrom(new ServerConfiguration()));
        h.Check("an untouched form estimates a complete offload, not an empty card",
            everything?.OffloadedBlocks == 4 && everything?.FullyOffloaded == true,
            $"{everything?.OffloadedBlocks}/{everything?.FullyOffloaded}");

        var half = VramEstimator.Estimate(model,
            VramPlan.RequestFrom(new ServerConfiguration { GpuLayers = 2 }));
        h.Check("a layer count typed into the form reaches the estimate",
            half?.OffloadedBlocks == 2 && half?.WeightBytes == 2 * GB,
            $"{half?.OffloadedBlocks}/{half?.WeightBytes}");

        var trained = VramEstimator.Estimate(model,
            VramPlan.RequestFrom(new ServerConfiguration { ContextSize = 0 }, model.MaxContext));
        var explicitCtx = VramEstimator.Estimate(model,
            VramPlan.RequestFrom(new ServerConfiguration { ContextSize = 8192 }));
        h.Check("-c 0 is sized against the trained context, not the server default",
            trained != null && explicitCtx != null && trained.KvBytes == explicitCtx.KvBytes,
            $"{trained?.KvBytes}/{explicitCtx?.KvBytes}");
    }

    private static void RunFit(Harness h)
    {
        h.Section("VramPlan: judging a model against the card");

        var model = Model(blocks: 4, blockBytes: GB);
        long need = VramEstimator.Estimate(model, VramPlan.RequestFrom(new ServerConfiguration()))!.TotalBytes;

        var roomy = VramPlan.Fit(model, Budget(need * 4));
        h.Check("a card with room to spare says so",
            roomy.Verdict == VramFit.Fits, roomy.Verdict.ToString());
        h.Check("and reports what the model would take",
            roomy.Bytes == need, roomy.Bytes.ToString());

        var exact = VramPlan.Fit(model, Budget(need + 1));
        h.Check("a card with a single byte to spare is tight, not comfortable",
            exact.Verdict == VramFit.Tight, exact.Verdict.ToString());

        var short_ = VramPlan.Fit(model, Budget(need - 1));
        h.Check("a byte short is a byte short",
            short_.Verdict == VramFit.DoesNotFit, short_.Verdict.ToString());
        h.Check("and the size is still worth reporting",
            short_.Bytes == need, short_.Bytes.ToString());

        h.Check("without a budget there is nothing to judge against",
            VramPlan.Fit(model, null).Verdict == VramFit.Unknown, "unknown");
        h.Check("a card that reported nothing at all is not a verdict either",
            VramPlan.Fit(model, Budget(0)).Verdict == VramFit.Unknown, "unknown");

        var full = VramPlan.Fit(model, new VramBudget
        {
            Config = new ServerConfiguration(),
            AvailableBytes = 0,
            TotalBytes = need * 4,
        });
        h.Check("a card known to be full fits nothing, which is a verdict of its own",
            full.Verdict == VramFit.DoesNotFit, full.Verdict.ToString());
        h.Check("and it still says what the model would have needed",
            full.Bytes == need, full.Bytes.ToString());
        h.Check("a file without a tensor table cannot be judged",
            VramPlan.Fit(new GgufModelInfo { BlockCount = 4 }, Budget(need * 4)).Verdict == VramFit.Unknown,
            "unknown");
        h.Check("and neither can no file at all",
            VramPlan.Fit(null, Budget(need * 4)).Verdict == VramFit.Unknown, "unknown");
        h.Check("an unknown verdict carries no size",
            VramPlan.Fit(model, null).Bytes == 0, "0");

        var partial = VramPlan.Fit(model, new VramBudget
        {
            Config = new ServerConfiguration { GpuLayers = 2 },
            AvailableBytes = need,
        });
        h.Check("the settings on the form are what gets judged",
            partial.Verdict == VramFit.Fits && partial.Bytes > 0 && partial.Bytes < need,
            $"{partial.Verdict}/{partial.Bytes}");

        var nothingOnTheCard = VramPlan.Fit(model, new VramBudget
        {
            Config = new ServerConfiguration { GpuLayers = 0 },
            AvailableBytes = need,
        });
        h.Check("a run that puts nothing on the card has no verdict to give",
            nothingOnTheCard.Verdict == VramFit.Unknown, nothingOnTheCard.Verdict.ToString());
    }

    private static void RunVerdict(Harness h)
    {
        h.Section("VramPlan: one rule for the panel and the list");

        long need = 4L * GB;
        h.Check("room to spare",
            VramPlan.Verdict(need, 16 * GB, 16 * GB) == VramFit.Fits, "fits");
        h.Check("barely enough",
            VramPlan.Verdict(need, need + 1, 16 * GB) == VramFit.Tight, "tight");
        h.Check("not enough",
            VramPlan.Verdict(need, need - 1, 16 * GB) == VramFit.DoesNotFit, "no");
        h.Check("a card with a known size and nothing free fits nothing",
            VramPlan.Verdict(need, 0, 16 * GB) == VramFit.DoesNotFit, "no");
        h.Check("a card that said nothing about itself yields no verdict",
            VramPlan.Verdict(need, 0, 0) == VramFit.Unknown, "unknown");
        h.Check("asking for nothing is not a verdict either",
            VramPlan.Verdict(0, 16 * GB, 16 * GB) == VramFit.Unknown, "unknown");
    }

    private static VramBudget Budget(long available) =>
        new() { Config = new ServerConfiguration(), AvailableBytes = available, TotalBytes = available };

    private static GgufModelInfo Model(int blocks, long blockBytes)
    {
        var blockList = new long[blocks];
        var kvList = new long[blocks];
        for (int i = 0; i < blocks; i++)
        {
            blockList[i] = blockBytes;
            kvList[i] = 1;
        }

        return new GgufModelInfo
        {
            Architecture = "test",
            BlockCount = blocks,
            MaxContext = 8192,
            HeadCount = 8,
            HeadCountKv = 8,
            KeyLength = 128,
            ValueLength = 128,
            EmbeddingLength = 1024,
            VocabSize = 32000,
            Tensors = new GgufTensorSummary
            {
                TensorCount = blocks * 3,
                TotalBytes = blocks * blockBytes + 512 * MB,
                RepeatingBytes = blocks * blockBytes,
                EmbeddingBytes = 256 * MB,
                OutputBytes = 256 * MB,
                BlockBytes = blockList,
                BlockExpertBytes = new long[blocks],
                BlockKvBytes = kvList,
            }
        };
    }
}
