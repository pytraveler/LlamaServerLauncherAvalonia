using System;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Services;

public static class ServerMemoryTests
{
    private const long MiB = 1024 * 1024;
    private const long GiB = 1024 * MiB;

    private static readonly string[] RealBreakdown =
    {
        "0.00.289.465 I common_memory_breakdown_print: | memory breakdown [MiB] | total    free    self   model   context   compute    unaccounted |",
        "0.00.289.467 I common_memory_breakdown_print: |   - CUDA0 (RTX 5090)   | 32606 = 30927 + (1354 =   603 +     448 +     302) +         325 |",
        "0.00.289.467 I common_memory_breakdown_print: |   - Host               |                   165 =   157 +       0 +       8                |",
    };

    private static readonly string[] FitBreakdown =
    {
        "0.01.338.793 I common_memory_breakdown_print: | memory breakdown [MiB] | total    free     self   model   context   compute    unaccounted |",
        "0.01.338.795 I common_memory_breakdown_print: |   - CUDA0 (RTX 5090)   | 32606 = 30737 + (19988 = 18347 +    1335 +     304) +      -18118 |",
        "0.01.338.795 I common_memory_breakdown_print: |   - Host               |                    559 =   515 +       0 +      44                |",
    };

    public static void Run(Harness h)
    {
        RunBuffers(h);
        RunBreakdown(h);
        RunAccumulator(h);
        RunComparison(h);
        RunProjector(h);
    }

    private static void RunProjector(Harness h)
    {
        h.Section("ServerMemoryParser: the vision projector");

        long worstCase = (long)Math.Round(1130.63 * MiB);

        h.Check("llama.cpp reports what the projector will cost",
            ServerMemoryParser.TryParseProjector(
                "srv    load_model: [mtmd] estimated worst-case memory usage of mmproj is 1130.63 MiB (took 32.83 ms)")
                == worstCase,
            worstCase.ToString());
        h.Check("a line about anything else is not that",
            ServerMemoryParser.TryParseProjector("load_tensors: CUDA0 model buffer size = 1130.63 MiB") == 0, "0");

        h.Check("the backend it runs on is named",
            ServerMemoryParser.TryParseProjectorDevice("clip_ctx: CLIP using CUDA0 backend") == "CUDA0", "CUDA0");
        h.Check("and can be the host",
            ServerMemoryParser.IsHostDevice(
                ServerMemoryParser.TryParseProjectorDevice("clip_ctx: CLIP using CPU backend")), "CPU");

        var onCard = new ServerMemoryAccumulator();
        onCard.Add("load_tensors: offloaded 41/41 layers to GPU");
        foreach (var line in Load("CUDA0", weights: 1024, kv: 512, compute: 256)) onCard.Add(line);
        onCard.Add("clip_ctx: CLIP using CUDA0 backend");
        onCard.Add("srv    load_model: [mtmd] estimated worst-case memory usage of mmproj is 1130.63 MiB (took 32.83 ms)");
        var card = onCard.Snapshot();
        h.Check("a projector on the card counts against the card",
            card.HasProjector && card.TotalBytes == (1024 + 512 + 256) * MiB + worstCase,
            card.TotalBytes.ToString());
        h.Check("and not against system memory",
            card.HostBytes == 0, card.HostBytes.ToString());

        var onHost = new ServerMemoryAccumulator();
        foreach (var line in Load("CUDA0", weights: 1024, kv: 512, compute: 256)) onHost.Add(line);
        onHost.Add("clip_ctx: CLIP using CPU backend");
        onHost.Add("srv    load_model: [mtmd] estimated worst-case memory usage of mmproj is 1130.63 MiB (took 32.83 ms)");
        var host = onHost.Snapshot();
        h.Check("a projector left in RAM does not",
            host.TotalBytes == (1024 + 512 + 256) * MiB, host.TotalBytes.ToString());
        h.Check("it is counted where it actually sits",
            host.ProjectorOnHost && host.HostBytes == worstCase, host.HostBytes.ToString());

        onCard.Reset();
        h.Check("a reset forgets the projector too",
            !onCard.Snapshot().HasProjector, "forgotten");

        var withProjector = new ServerMemoryReport
        {
            Source = ServerMemorySource.Breakdown,
            WeightBytes = 25 * GiB,
            ProjectorBytes = worstCase,
        };
        h.Check("the comparison line names the projector",
            VramComparison.Describe("p", withProjector, null).Contains("projector 1.10"), "named");
        h.Check("but not when it stayed on the CPU",
            !VramComparison.Describe("p", withProjector with { ProjectorOnHost = true }, null).Contains("projector"),
            "quiet");
    }

    private static void RunBuffers(Harness h)
    {
        h.Section("ServerMemoryParser: the buffer lines llama.cpp prints");

        var weights = ServerMemoryParser.TryParseBuffer(
            "0.00.477.428 I load_tensors:        CUDA0 model buffer size =  1024.00 MiB");
        h.Check("a model buffer is weights on the named device",
            weights?.Kind == ServerBufferKind.Weights && weights?.Device == "CUDA0",
            $"{weights?.Kind}/{weights?.Device}");
        h.Check("and MiB are binary megabytes",
            weights?.Bytes == GiB, weights?.Bytes.ToString() ?? "null");
        h.Check("a card is not the host",
            weights?.OnHost == false, (weights?.OnHost)?.ToString() ?? "null");

        var mapped = ServerMemoryParser.TryParseBuffer(
            "0.00.477.414 I load_tensors:   CPU_Mapped model buffer size =   512.00 MiB");
        h.Check("memory mapped weights sit on the host",
            mapped?.OnHost == true && mapped?.Bytes == 512 * MiB,
            $"{mapped?.OnHost}/{mapped?.Bytes}");

        var kv = ServerMemoryParser.TryParseBuffer(
            "0.00.627.597 I llama_kv_cache:      CUDA0 KV buffer size =  3072.00 MiB");
        h.Check("a KV buffer is cache",
            kv?.Kind == ServerBufferKind.Cache && kv?.Bytes == 3 * GiB,
            $"{kv?.Kind}/{kv?.Bytes}");

        var recurrent = ServerMemoryParser.TryParseBuffer(
            "llama_memory_recurrent:      CUDA0 RS buffer size =    16.00 MiB");
        h.Check("so is the recurrent state of a hybrid model",
            recurrent?.Kind == ServerBufferKind.Cache && recurrent?.Bytes == 16 * MiB,
            $"{recurrent?.Kind}/{recurrent?.Bytes}");

        var lora = ServerMemoryParser.TryParseBuffer(
            "llama_adapter_lora_init_impl:      CUDA0 LoRA buffer size =    64.00 MiB");
        h.Check("an adapter counts with the weights it is bolted onto",
            lora?.Kind == ServerBufferKind.Weights, lora?.Kind.ToString() ?? "null");

        var layers = ServerMemoryParser.TryParseLayers("load_tensors: offloaded 41/41 layers to GPU");
        h.Check("the layer split is read off the load line",
            layers?.Offloaded == 41 && layers?.Total == 41, $"{layers?.Offloaded}/{layers?.Total}");

        var partial = ServerMemoryParser.TryParseLayers(
            "0.00.477.428 I load_tensors: offloaded 20/49 layers to GPU");
        h.Check("a partial offload reads just as well",
            partial?.Offloaded == 20 && partial?.Total == 49, $"{partial?.Offloaded}/{partial?.Total}");

        h.Check("the line about repeating layers is not the count",
            ServerMemoryParser.TryParseLayers("load_tensors: offloading 39 repeating layers to GPU") == null,
            "null");
        h.Check("and nonsense is not a count either",
            ServerMemoryParser.TryParseLayers("load_tensors: offloaded 50/0 layers to GPU") == null,
            "null");

        var compute = ServerMemoryParser.TryParseBuffer(
            "0.00.635.819 I sched_reserve:      CUDA0 compute buffer size =   304.00 MiB");
        h.Check("a compute buffer is a compute buffer",
            compute?.Kind == ServerBufferKind.Compute && compute?.Bytes == 304 * MiB,
            $"{compute?.Kind}/{compute?.Bytes}");

        var output = ServerMemoryParser.TryParseBuffer(
            "0.00.627.101 I llama_context:  CUDA_Host  output buffer size =     2.00 MiB");
        h.Check("the output buffer belongs to the context, not the weights",
            output?.Kind == ServerBufferKind.Compute && output?.OnHost == true,
            $"{output?.Kind}/{output?.OnHost}");

        h.Check("plain CPU is the host",
            ServerMemoryParser.IsHostDevice("CPU"), "host");
        h.Check("so is the row the breakdown calls Host",
            ServerMemoryParser.IsHostDevice("Host"), "host");
        h.Check("so is the BLAS backend",
            ServerMemoryParser.IsHostDevice("BLAS"), "host");
        h.Check("a second card is not",
            !ServerMemoryParser.IsHostDevice("CUDA1"), "device");
        h.Check("nor is Metal",
            !ServerMemoryParser.IsHostDevice("Metal"), "device");

        h.Check("a line about something else is not a buffer",
            ServerMemoryParser.TryParseBuffer("slot print_timing: 36.10 tokens per second") == null, "null");
        h.Check("neither is an empty line",
            ServerMemoryParser.TryParseBuffer("") == null, "null");
        h.Check("nor no line at all",
            ServerMemoryParser.TryParseBuffer(null) == null, "null");
        h.Check("a buffer line without a size is not usable",
            ServerMemoryParser.TryParseBuffer("sched_reserve: CUDA0 compute buffer size = ") == null, "null");

        var fractional = ServerMemoryParser.TryParseBuffer(
            "sched_reserve:      CUDA0 compute buffer size =     0.50 MiB");
        h.Check("half a megabyte is half a megabyte",
            fractional?.Bytes == 512 * 1024, fractional?.Bytes.ToString() ?? "null");
    }

    private static void RunBreakdown(Harness h)
    {
        h.Section("ServerMemoryParser: the breakdown table llama.cpp prints");

        h.Check("the header is recognised as a header",
            ServerMemoryParser.IsBreakdownHeader(RealBreakdown[0]), "header");
        h.Check("and is not read as a row",
            ServerMemoryParser.TryParseBreakdownRow(RealBreakdown[0]) == null, "null");

        var card = ServerMemoryParser.TryParseBreakdownRow(RealBreakdown[1]);
        h.Check("the card row names the device without its marketing name",
            card?.Device == "CUDA0" && card?.OnHost == false, $"{card?.Device}/{card?.OnHost}");
        h.Check("weights, cache and compute are read in order",
            card?.WeightBytes == 603 * MiB && card?.CacheBytes == 448 * MiB
                && card?.ComputeBytes == 302 * MiB,
            $"{card?.WeightBytes}/{card?.CacheBytes}/{card?.ComputeBytes}");
        h.Check("what llama.cpp cannot account for is what the estimate calls headroom",
            card?.UnaccountedBytes == 325 * MiB, card?.UnaccountedBytes.ToString() ?? "null");
        h.Check("the row also says how big the card is and how much of it was free",
            card?.DeviceTotalBytes == 32606L * MiB && card?.DeviceFreeBytes == 30927L * MiB,
            $"{card?.DeviceTotalBytes}/{card?.DeviceFreeBytes}");

        var host = ServerMemoryParser.TryParseBreakdownRow(RealBreakdown[2]);
        h.Check("the host row is read as host memory",
            host?.OnHost == true && host?.WeightBytes == 157 * MiB,
            $"{host?.OnHost}/{host?.WeightBytes}");
        h.Check("and carries no card size of its own",
            host?.DeviceTotalBytes == 0 && host?.UnaccountedBytes == 0,
            $"{host?.DeviceTotalBytes}/{host?.UnaccountedBytes}");

        var fit = ServerMemoryParser.TryParseBreakdownRow(FitBreakdown[1]);
        h.Check("the fitting pass counts memory it has not taken yet, so its row goes negative",
            fit?.WeightBytes == 18347 * MiB && fit?.UnaccountedBytes == 0,
            $"{fit?.WeightBytes}/{fit?.UnaccountedBytes}");
        h.Check("and the card behind it is still read correctly",
            fit?.DeviceTotalBytes == 32606 * MiB && fit?.DeviceFreeBytes == 30737 * MiB,
            $"{fit?.DeviceTotalBytes}/{fit?.DeviceFreeBytes}");

        h.Check("a table line is required to come from the breakdown printer",
            ServerMemoryParser.TryParseBreakdownRow("srv  slots: |  - idle  | 4 = 1 + 2 + 1 |") == null,
            "null");
        h.Check("a line without a table in it is not a row",
            ServerMemoryParser.TryParseBreakdownRow("common_memory_breakdown_print: nothing here") == null,
            "null");
    }

    private static void RunAccumulator(Harness h)
    {
        h.Section("ServerMemoryAccumulator: one load, one report");

        var acc = new ServerMemoryAccumulator();
        h.Check("a line that says nothing is not counted",
            !acc.Add("srv  load_model: loading model"), "false");
        h.Check("and an empty run has nothing to report",
            !acc.Snapshot().HasAny, "empty");

        acc.Add("load_tensors:   CPU_Mapped model buffer size =   128.00 MiB");
        foreach (var line in Load("CUDA0", weights: 1024, kv: 512, compute: 256)) acc.Add(line);
        acc.Add("sched_reserve:  CUDA_Host compute buffer size =    32.00 MiB");

        var report = acc.Snapshot();
        h.Check("without a breakdown the buffer lines are what we have",
            report.Source == ServerMemorySource.Buffers, report.Source.ToString());
        h.Check("what went to the card is summed apart from what stayed in RAM",
            report.WeightBytes == 1024 * MiB && report.HostWeightBytes == 128 * MiB,
            $"{report.WeightBytes}/{report.HostWeightBytes}");
        h.Check("the cache is counted",
            report.CacheBytes == 512 * MiB, report.CacheBytes.ToString());
        h.Check("and so are the buffers, on both sides",
            report.ComputeBytes == 256 * MiB && report.HostComputeBytes == 32 * MiB,
            $"{report.ComputeBytes}/{report.HostComputeBytes}");
        h.Check("the total is what the card holds, not what the host does",
            report.TotalBytes == (1024 + 512 + 256) * MiB, report.TotalBytes.ToString());
        h.Check("only real devices are counted as devices",
            report.DeviceCount == 1, report.DeviceCount.ToString());

        var twoCards = new ServerMemoryAccumulator();
        foreach (var line in Load("CUDA0", weights: 1024, kv: 512, compute: 256)) twoCards.Add(line);
        twoCards.Add("load_tensors:        CUDA1 model buffer size =  2048.00 MiB");
        twoCards.Add("llama_kv_cache:      CUDA1 KV buffer size =   512.00 MiB");
        var split = twoCards.Snapshot();
        h.Check("two cards add up",
            split.WeightBytes == 3072 * MiB && split.CacheBytes == 1024 * MiB,
            $"{split.WeightBytes}/{split.CacheBytes}");
        h.Check("and are reported as two",
            split.DeviceCount == 2, split.DeviceCount.ToString());

        var real = new ServerMemoryAccumulator();
        foreach (var line in Load("CUDA0", weights: 0, kv: 0, compute: 302)) real.Add(line);
        foreach (var line in RealBreakdown) real.Add(line);
        foreach (var line in Load("CUDA0", weights: 603, kv: 448, compute: 302)) real.Add(line);
        var measured = real.Snapshot();
        h.Check("a breakdown outranks the buffer lines, so the fitting pass cannot double count",
            measured.Source == ServerMemorySource.Breakdown
                && measured.ComputeBytes == 302 * MiB,
            $"{measured.Source}/{measured.ComputeBytes}");
        h.Check("and the whole footprint includes what llama.cpp could not account for",
            measured.TotalBytes == (603 + 448 + 302 + 325) * MiB, measured.TotalBytes.ToString());
        h.Check("the host side comes from the same table",
            measured.HostBytes == 165 * MiB, measured.HostBytes.ToString());

        var refitted = new ServerMemoryAccumulator();
        foreach (var line in RealBreakdown) refitted.Add(line);
        refitted.Add(RealBreakdown[0]);
        refitted.Add("common_memory_breakdown_print: |   - CUDA0 (RTX 5090)   | 32606 = 30000 + (2000 =  1000 +     500 +     500) +         100 |");
        var last = refitted.Snapshot();
        h.Check("a second table replaces the first rather than adding to it",
            last.WeightBytes == 1000 * MiB && last.UnaccountedBytes == 100 * MiB,
            $"{last.WeightBytes}/{last.UnaccountedBytes}");

        var midFit = new ServerMemoryAccumulator();
        midFit.Add("load_tensors:        CUDA0 model buffer size =     0.00 MiB");
        midFit.Add("load_tensors:    CUDA_Host model buffer size =     0.00 MiB");
        midFit.Add("llama_context:  CUDA_Host  output buffer size =     2.33 MiB");
        midFit.Add("llama_kv_cache:      CUDA0 KV buffer size =     0.00 MiB");
        var partway = midFit.Snapshot();
        h.Check("the fitting pass allocates nothing on the card, so there is nothing to report yet",
            !partway.HasAny, $"{partway.Source}/{partway.TotalBytes}");
        h.Check("even though a host buffer has already been seen",
            partway.HostBytes > 0, partway.HostBytes.ToString());

        var layered = new ServerMemoryAccumulator();
        layered.Add("load_tensors: offloaded 20/49 layers to GPU");
        foreach (var line in RealBreakdown) layered.Add(line);
        layered.Add("load_tensors: offloaded 49/49 layers to GPU");
        var withLayers = layered.Snapshot();
        h.Check("the layer split rides along with the memory",
            withLayers.HasLayers && withLayers.OffloadedLayers == 49 && withLayers.TotalLayers == 49,
            $"{withLayers.OffloadedLayers}/{withLayers.TotalLayers}");

        var refit = new ServerMemoryAccumulator();
        refit.Add("load_tensors: offloaded 41/41 layers to GPU");
        foreach (var line in Load("CUDA0", weights: 0, kv: 0, compute: 304)) refit.Add(line);
        foreach (var line in FitBreakdown) refit.Add(line);
        refit.Add("load_tensors: offloaded 41/41 layers to GPU");
        foreach (var line in Load("CUDA0", weights: 18347, kv: 1147, compute: 304)) refit.Add(line);
        refit.Add("reserve_compute_meta:      CUDA0 compute buffer size =   248.00 MiB");
        var afterFit = refit.Snapshot();
        h.Check("a real load starts the count over, so the fitting pass leaves nothing behind",
            afterFit.Source == ServerMemorySource.Buffers && afterFit.WeightBytes == 18347 * MiB,
            $"{afterFit.Source}/{afterFit.WeightBytes}");
        h.Check("the cache is the one that was really taken",
            afterFit.CacheBytes == 1147 * MiB, afterFit.CacheBytes.ToString());
        h.Check("both compute reservations of that load count",
            afterFit.ComputeBytes == (304 + 248) * MiB, afterFit.ComputeBytes.ToString());
        h.Check("and the layer count survives the restart",
            afterFit.OffloadedLayers == 41 && afterFit.TotalLayers == 41,
            $"{afterFit.OffloadedLayers}/{afterFit.TotalLayers}");

        var cleared = new ServerMemoryAccumulator();
        foreach (var line in RealBreakdown) cleared.Add(line);
        cleared.Add("load_tensors: offloaded 49/49 layers to GPU");
        cleared.Reset();
        h.Check("a cleared accumulator reports nothing",
            !cleared.Snapshot().HasAny, "empty");
        h.Check("and has forgotten the layers too",
            !cleared.Snapshot().HasLayers, "forgotten");
    }

    private static void RunComparison(Harness h)
    {
        h.Section("VramComparison: what was promised against what was taken");

        var report = new ServerMemoryReport
        {
            Source = ServerMemorySource.Breakdown,
            WeightBytes = 25 * GiB,
            CacheBytes = GiB,
            ComputeBytes = 512 * MiB,
            UnaccountedBytes = 325 * MiB,
            HostWeightBytes = 256 * MiB,
        };

        var estimate = new VramEstimate
        {
            WeightBytes = 25 * GiB,
            KvBytes = GiB,
            ComputeBytes = 512 * MiB,
            OverheadBytes = 400 * MiB,
        };

        var text = VramComparison.Describe("profile", report, estimate);
        h.Check("the line names the profile it is about",
            text.Contains("'profile'"), text);
        h.Check("it reports what the server took",
            text.Contains("26.82 GB on the card"), text);
        h.Check("it reports the headroom llama.cpp measured",
            text.Contains("headroom 0.32"), text);
        h.Check("it mentions what stayed in system memory",
            text.Contains("0.25 GB in system memory"), text);
        h.Check("it reports what the estimate said",
            text.Contains("26.89 GB"), text);
        h.Check("and by how much the estimate was over",
            text.Contains("0.07 GB over"), text);

        var underEstimate = estimate with { WeightBytes = 20 * GiB };
        h.Check("an estimate below what happened is called low",
            VramComparison.Describe("p", report, underEstimate).Contains("under"), "under");

        h.Check("without an estimate the measurement still gets reported",
            VramComparison.Describe("p", report, null).Contains("26.82 GB on the card"), "reported");

        h.Check("a headroom nobody measured is not printed",
            !VramComparison.Describe("p", report with { UnaccountedBytes = 0 }, estimate)
                .Contains("headroom 0.00"), "quiet");

        h.Check("the layer split is named when llama.cpp reported it",
            VramComparison.Describe("p", report with { OffloadedLayers = 40, TotalLayers = 49 }, estimate)
                .Contains("layers 40/49"), "named");
        h.Check("and left out when it did not",
            !VramComparison.Describe("p", report, estimate).Contains("layers"), "quiet");

        h.Check("a single card is not worth spelling out",
            !VramComparison.Describe("p", report, estimate).Contains("spread over"), "quiet");
        h.Check("but several are",
            VramComparison.Describe("p", report with { DeviceCount = 2 }, estimate).Contains("spread over 2"),
            "named");
    }

    private static string[] Load(string device, int weights, int kv, int compute) => new[]
    {
        $"load_tensors:        {device} model buffer size = {weights}.00 MiB",
        $"llama_kv_cache:      {device} KV buffer size = {kv}.00 MiB",
        $"sched_reserve:       {device} compute buffer size = {compute}.00 MiB",
    };
}
