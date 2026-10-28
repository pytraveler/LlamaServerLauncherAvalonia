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
        RunUnsetContext(h);
        RunFitContext(h);
    }

    private static void RunUnsetContext(Harness h)
    {
        h.Section("VramPlan: an empty context, the way llama-server picks it");

        var empty = new ServerConfiguration();
        var model = VramPlan.ChooseContext(empty, 131072);
        h.Check("an empty field takes the context the model was trained for",
            model.Context == 131072, model.Context.ToString());
        h.Check("and --fit may shrink it down to 4096",
            model.FitFloor == 4096, model.FitFloor?.ToString() ?? "null");
        h.Check("the request says the same",
            VramPlan.RequestFrom(empty, 131072).ContextSize == 131072, VramPlan.RequestFrom(empty, 131072).ContextSize.ToString());

        var old = new ContextDefaults { UnsetContext = 4096, Fit = false };
        var oldBuild = VramPlan.ChooseContext(empty, 131072, old);
        h.Check("a build whose help still says 4096 gets 4096, with nothing to shrink",
            oldBuild.Context == 4096 && oldBuild.FitFloor == null, $"{oldBuild.Context}/{oldBuild.FitFloor}");

        var noFit = VramPlan.ChooseContext(empty, 131072, new ContextDefaults { Fit = false });
        h.Check("a build without --fit takes the model context and leaves it be",
            noFit.Context == 131072 && noFit.FitFloor == null, $"{noFit.Context}/{noFit.FitFloor}");

        var slots = VramPlan.ChooseContext(new ServerConfiguration { ParallelSlots = 2 }, 131072);
        h.Check("two slots without a unified cache each get the full context, as --fit sizes them",
            slots.Context == 2 * 131072 && slots.FitFloor == 2 * 4096, $"{slots.Context}/{slots.FitFloor}");

        var unified = VramPlan.ChooseContext(new ServerConfiguration { ParallelSlots = 2, CustomArguments = "-kvu" }, 131072);
        h.Check("a unified cache shares one context between them",
            unified.Context == 131072 && unified.FitFloor == 4096, $"{unified.Context}/{unified.FitFloor}");

        var off = VramPlan.ChooseContext(new ServerConfiguration { ParallelSlots = 2, CustomArguments = "--fit off" }, 131072);
        h.Check("with --fit off the model context is taken once and not shrunk",
            off.Context == 131072 && off.FitFloor == null, $"{off.Context}/{off.FitFloor}");

        var offDisabled = VramPlan.ChooseContext(new ServerConfiguration
        {
            CustomArguments = "-fit off",
            CustomArgumentToggleStates = new() { ["-fit"] = false },
        }, 131072);
        h.Check("a --fit off that is switched off in the list does not count",
            offDisabled.FitFloor == 4096, offDisabled.FitFloor?.ToString() ?? "null");

        var floor = VramPlan.ChooseContext(new ServerConfiguration { CustomArguments = "--fit-ctx 16384" }, 131072);
        h.Check("--fit-ctx moves the floor",
            floor.FitFloor == 16384, floor.FitFloor?.ToString() ?? "null");

        var small = VramPlan.ChooseContext(empty, 2048);
        h.Check("a model trained on less than the floor keeps what it was trained on",
            small.Context == 2048 && small.FitFloor == 2048, $"{small.Context}/{small.FitFloor}");

        var zero = VramPlan.ChooseContext(new ServerConfiguration { ContextSize = 0 }, 131072);
        h.Check("-c 0 asks for the full context, and --fit leaves it be",
            zero.Context == 131072 && zero.FitFloor == null, $"{zero.Context}/{zero.FitFloor}");

        var zeroSlots = VramPlan.ChooseContext(new ServerConfiguration { ContextSize = 0, ParallelSlots = 3 }, 131072);
        h.Check("with -c 0 and three separate slots every slot gets the full context",
            zeroSlots.Context == 3 * 131072 && zeroSlots.FitFloor == null, $"{zeroSlots.Context}/{zeroSlots.FitFloor}");

        var pinned = VramPlan.ChooseContext(new ServerConfiguration { ContextSize = 8192 }, 131072);
        h.Check("a context of its own is not touched",
            pinned.Context == 8192 && pinned.FitFloor == null, $"{pinned.Context}/{pinned.FitFloor}");

        var perSlot = VramPlan.ChooseContext(new ServerConfiguration { ContextPerSlot = 8192 }, 131072);
        h.Check("a per-slot limit sizes the pool before --fit looks at it",
            perSlot.Context == 4 * 8192 && perSlot.FitFloor == null, $"{perSlot.Context}/{perSlot.FitFloor}");

        var unknown = VramPlan.ChooseContext(empty, null);
        h.Check("without the trained context in the file the plan falls back to 4096",
            unknown.Context == 4096 && unknown.FitFloor == null, $"{unknown.Context}/{unknown.FitFloor}");
    }

    private static void RunFitContext(Harness h)
    {
        h.Section("VramPlan: shrinking the context like --fit");

        var model = Model(blocks: 4, blockBytes: GB, maxContext: 131072);
        var empty = new ServerConfiguration();
        var request = VramPlan.RequestFrom(empty, model.MaxContext);
        long fullNeed = VramEstimator.Estimate(model, request)!.TotalBytes;
        long floorNeed = VramEstimator.Estimate(model, request with { ContextSize = 4096 })!.TotalBytes;
        long margin = VramPlan.DefaultFitTargetBytes;
        long available = (floorNeed + fullNeed) / 2 + margin;

        var fitted = VramPlan.FitContext(model, request, 4096, available);
        h.Check("a card that cannot hold the full context gets a smaller one",
            fitted.ContextSize > 4096 && fitted.ContextSize < 131072, fitted.ContextSize.ToString());
        h.Check("rounded down the way llama.cpp rounds it",
            fitted.ContextSize % VramPlan.FitContextStep == 0, fitted.ContextSize.ToString());
        long fittedNeed = VramEstimator.Estimate(model, fitted)!.TotalBytes;
        h.Check("and it leaves the --fit target free, as llama.cpp does",
            fittedNeed + margin <= available, $"{fittedNeed} + {margin} / {available}");
        long overNeed = VramEstimator.Estimate(model, fitted with { ContextSize = fitted.ContextSize + 2 * VramPlan.FitContextStep })!.TotalBytes;
        h.Check("while a little more would eat into it",
            overNeed + margin > available, $"{overNeed} + {margin} / {available}");

        var wider = VramPlan.FitContext(model, request, 4096, available, 0);
        h.Check("a smaller --fit-target leaves room for more context",
            wider.ContextSize > fitted.ContextSize, $"{wider.ContextSize}/{fitted.ContextSize}");
        h.Check("the target is read from -fitt, first device first",
            VramPlan.FitTargetBytes(new ServerConfiguration { CustomArguments = "-fitt 512,2048" }) == 512 * MB
            && VramPlan.FitTargetBytes(new ServerConfiguration()) == margin,
            VramPlan.FitTargetBytes(new ServerConfiguration { CustomArguments = "-fitt 512,2048" }).ToString());
        h.Check("a card smaller than the target still shrinks to the floor",
            VramPlan.FitContext(model, request, 4096, margin / 2).ContextSize == 4096,
            VramPlan.FitContext(model, request, 4096, margin / 2).ContextSize.ToString());

        h.Check("a card with room keeps the full context",
            VramPlan.FitContext(model, request, 4096, fullNeed * 2).ContextSize == 131072,
            VramPlan.FitContext(model, request, 4096, fullNeed * 2).ContextSize.ToString());
        h.Check("a card too small even for the floor stops at the floor",
            VramPlan.FitContext(model, request, 4096, floorNeed / 2).ContextSize == 4096,
            VramPlan.FitContext(model, request, 4096, floorNeed / 2).ContextSize.ToString());
        h.Check("without a floor nothing is shrunk",
            VramPlan.FitContext(model, request, null, available).ContextSize == 131072,
            VramPlan.FitContext(model, request, null, available).ContextSize.ToString());
        h.Check("and without a reading from the card nothing is either",
            VramPlan.FitContext(model, request, 4096, 0).ContextSize == 131072,
            VramPlan.FitContext(model, request, 4096, 0).ContextSize.ToString());

        var listed = VramPlan.Fit(model, new VramBudget { Config = empty, AvailableBytes = available, TotalBytes = available });
        h.Check("the model list judges an empty field the same way, and calls it a fit, since --fit fills the card on purpose",
            listed.Verdict == VramFit.Fits && listed.Bytes == fittedNeed, $"{listed.Verdict}/{listed.Bytes}/{fittedNeed}");

        h.Check("a context shrunk by --fit that lands within the card is a fit",
            VramPlan.FittedVerdict(60000, 131072, 4096, 97 * GB, 100 * GB, 100 * GB) == VramFit.Fits, "fits");
        h.Check("one stuck at the floor is judged as usual",
            VramPlan.FittedVerdict(4096, 131072, 4096, 97 * GB, 100 * GB, 100 * GB) == VramFit.Tight, "tight");
        h.Check("and so is a context nobody shrunk",
            VramPlan.FittedVerdict(131072, 131072, 4096, 97 * GB, 100 * GB, 100 * GB) == VramFit.Tight
            && VramPlan.FittedVerdict(60000, 131072, null, 97 * GB, 100 * GB, 100 * GB) == VramFit.Tight, "tight");
        h.Check("a shrunk context that still does not fit does not become a fit",
            VramPlan.FittedVerdict(60000, 131072, 4096, 101 * GB, 100 * GB, 100 * GB) == VramFit.DoesNotFit, "no");

        h.Check("a running server with an empty field is counted at the context it settled on",
            VramPlan.RunningRequest(empty, 131072, 52224).ContextSize == 52224,
            VramPlan.RunningRequest(empty, 131072, 52224).ContextSize.ToString());
        h.Check("before it says, at the full one",
            VramPlan.RunningRequest(empty, 131072, 0).ContextSize == 131072,
            VramPlan.RunningRequest(empty, 131072, 0).ContextSize.ToString());
        h.Check("a context of its own needs no word from the server",
            VramPlan.RunningRequest(new ServerConfiguration { ContextSize = 8192 }, 131072, 52224).ContextSize == 8192,
            VramPlan.RunningRequest(new ServerConfiguration { ContextSize = 8192 }, 131072, 52224).ContextSize.ToString());
        h.Check("and neither does a per-slot limit, whose slot line shows the cap and not the pool",
            VramPlan.RunningRequest(new ServerConfiguration { ContextPerSlot = 4096, ParallelSlots = 2 }, 131072, 4096).ContextSize == 8192,
            VramPlan.RunningRequest(new ServerConfiguration { ContextPerSlot = 4096, ParallelSlots = 2 }, 131072, 4096).ContextSize.ToString());

        var listedOld = VramPlan.Fit(model, new VramBudget
        {
            Config = empty,
            AvailableBytes = fullNeed - 1,
            TotalBytes = fullNeed - 1,
            Context = new ContextDefaults { Fit = false },
        });
        h.Check("and a build without --fit is judged on the full context, which does not shrink to fit",
            listedOld.Verdict == VramFit.DoesNotFit && listedOld.Bytes == fullNeed, $"{listedOld.Verdict}/{listedOld.Bytes}");
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
        h.Check("no moe cache and no ffn layers on the host",
            empty.MoeCacheBytes == 0 && empty.CpuFfnBlocks == 0, $"{empty.MoeCacheBytes}/{empty.CpuFfnBlocks}");

        var missing = VramPlan.RequestFrom(null);
        h.Check("no configuration at all reads like an empty one",
            missing.ContextSize == 4096 && missing.GpuLayers == -1,
            $"{missing.ContextSize}/{missing.GpuLayers}");

        var filled = VramPlan.RequestFrom(new ServerConfiguration
        {
            ContextSize = 32768,
            GpuLayers = 20,
            CpuMoe = 6,
            MoeCacheMib = 3072,
            CpuFfn = 5,
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
        h.Check("the moe cache is read in MiB",
            filled.MoeCacheBytes == 3072L * 1024 * 1024, filled.MoeCacheBytes.ToString());
        h.Check("the ffn layer count is carried over",
            filled.CpuFfnBlocks == 5, filled.CpuFfnBlocks.ToString());
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

        var perSlotAuto = VramPlan.RequestFrom(new ServerConfiguration { ContextPerSlot = 8192 });
        h.Check("a context per slot without -c sizes the pool for the four auto slots",
            perSlotAuto.ContextSize == 4 * 8192, perSlotAuto.ContextSize.ToString());

        var perSlotTwo = VramPlan.RequestFrom(new ServerConfiguration { ContextPerSlot = 8192, ParallelSlots = 2 });
        h.Check("and for the slots that were asked for",
            perSlotTwo.ContextSize == 2 * 8192, perSlotTwo.ContextSize.ToString());

        var perSlotPinned = VramPlan.RequestFrom(new ServerConfiguration { ContextPerSlot = 8192, ParallelSlots = 2, ContextSize = 65536 });
        h.Check("a context of its own only caps the slots, the pool stays as set",
            perSlotPinned.ContextSize == 65536, perSlotPinned.ContextSize.ToString());

        var noFit = new ContextDefaults { Fit = false };
        h.Check("-c 0 is pinned too, so the per-slot limit does not size the pool",
            VramPlan.ResolveContext(0, 262144, 8192, 2, noFit) == 262144, VramPlan.ResolveContext(0, 262144, 8192, 2, noFit).ToString());
        h.Check("a zero per-slot limit is no limit",
            VramPlan.ResolveContext(null, null, 0, 2) == 4096, VramPlan.ResolveContext(null, null, 0, 2).ToString());
        h.Check("a huge per-slot limit does not wrap around",
            VramPlan.ResolveContext(null, null, int.MaxValue, 64) == int.MaxValue, VramPlan.ResolveContext(null, null, int.MaxValue, 64).ToString());
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

        long card = 32L * GB;
        long jitter = 100 * MB;
        h.Check("a first reading is taken as it comes",
            VramPlan.Settle(0, 20 * GB, card) == 20 * GB, "taken");
        h.Check("a reading that only jitters leaves the figure where it was",
            VramPlan.Settle(20 * GB, 20 * GB + jitter, card) == 20 * GB, "held");
        h.Check("and so does one that jitters the other way",
            VramPlan.Settle(20 * GB, 20 * GB - jitter, card) == 20 * GB, "held");
        h.Check("a server taking the card moves it at once",
            VramPlan.Settle(20 * GB, 2 * GB, card) == 2 * GB, "moved");
        h.Check("a server letting go of the card moves it back",
            VramPlan.Settle(2 * GB, 20 * GB, card) == 20 * GB, "moved");
        long deadband = card / 100 * VramPlan.SettlePercent;
        h.Check("the deadband is two percent of the card",
            VramPlan.Settle(20 * GB, 20 * GB + deadband, card) == 20 * GB + deadband, "moved");
        h.Check("and one byte below it is still a hold",
            VramPlan.Settle(20 * GB, 20 * GB + deadband - 1, card) == 20 * GB, "held");
        h.Check("a small card still gets a floor under the deadband",
            VramPlan.Settle(4 * GB, 4 * GB + 200 * MB, 6 * GB) == 4 * GB, "held");
        h.Check("losing the reading is not something to sit on",
            VramPlan.Settle(20 * GB, 0, card) == 0, "dropped");

        h.Check("with nothing running the free reading is what there is",
            VramPlan.Available(9 * GB, 0, card) == 9 * GB, "as read");
        h.Check("a restart hands back what the running server holds",
            VramPlan.Available(9 * GB, 20 * GB, card) == 29 * GB, "reclaimed");
        h.Check("but never more than the card has",
            VramPlan.Available(9 * GB, 30 * GB, card) == card, "capped");
        h.Check("without a card size there is nothing to cap against",
            VramPlan.Available(9 * GB, 30 * GB, 0) == 39 * GB, "uncapped");
        h.Check("nonsense readings do not subtract",
            VramPlan.Available(-5 * GB, -5 * GB, card) == 0, "floored");

        h.Check("a projector nobody chose costs nothing",
            VramPlan.ProjectorBytes(new ServerConfiguration(), GB) == 0, "none");
        h.Check("a chosen projector is counted",
            VramPlan.ProjectorBytes(new ServerConfiguration { MmprojPath = "p.gguf" }, GB) == GB, "counted");
        h.Check("one kept in system memory is not",
            VramPlan.ProjectorBytes(
                new ServerConfiguration { MmprojPath = "p.gguf", MmprojOffload = false }, GB) == 0, "off the card");
        h.Check("auto means the card, the way llama.cpp defaults",
            VramPlan.ProjectorBytes(
                new ServerConfiguration { MmprojPath = "p.gguf", MmprojOffload = null }, GB) == GB, "counted");
        h.Check("the request carries it",
            VramPlan.RequestFrom(new ServerConfiguration { MmprojPath = "p.gguf" }, null, GB).MmprojBytes == GB,
            "carried");

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
            Config = new ServerConfiguration { ContextSize = 4096 },
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
        new() { Config = new ServerConfiguration { ContextSize = 4096 }, AvailableBytes = available, TotalBytes = available };

    private static GgufModelInfo Model(int blocks, long blockBytes, int maxContext = 8192)
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
            MaxContext = maxContext,
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
