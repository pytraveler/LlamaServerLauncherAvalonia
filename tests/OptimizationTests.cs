using System;
using System.Collections.Generic;
using System.Linq;
using LlamaServerLauncher.Optimization.Distributions;
using LlamaServerLauncher.Optimization.Samplers;
using LlamaServerLauncher.Optimization.Storage;
using LlamaServerLauncher.Optimization.Study;
using LlamaServerLauncher.Optimization.Trial;
using LlamaServerLauncher.Models.Optimization;
using LlamaServerLauncher.Services.Optimization;

public static class OptimizationTests
{
    public static void Run(Harness h)
    {
        Stage1RandomCore(h);
        Stage2Grid(h);
        Stage2TpeAcceptance(h);
        Stage3CsvParser(h);
        Stage4CommandBuilders(h);
        VariantBServerArgv(h);
    }

    private static double Quadratic2D(Trial t)
    {
        double x = t.SuggestFloat("x", -10.0, 10.0);
        double y = t.SuggestFloat("y", -10.0, 10.0);
        return (x - 2.0) * (x - 2.0) + (y + 3.0) * (y + 3.0);
    }

    private static double RunStudy(ISampler sampler, Func<Trial, double> obj, int nTrials)
    {
        var st = Study.Create(new InMemoryStorage(), sampler, StudyDirection.Minimize);
        st.Optimize(obj, nTrials);
        return st.BestValue;
    }

    private static void Stage1RandomCore(Harness h)
    {
        h.Section("Stage 1: RandomSampler core");
        {
            var storage = new InMemoryStorage();
            var study = Study.Create(storage, new RandomSampler(seed: 42), StudyDirection.Minimize);
            study.Optimize(Quadratic2D, nTrials: 400);
            var best = study.BestTrial!;
            double bx = Convert.ToDouble(best.Params["x"]);
            double by = Convert.ToDouble(best.Params["y"]);
            Console.WriteLine($"  best value={best.Value:F4} at x={bx:F3}, y={by:F3} (number={best.Number})");
            h.Check("Random converges near optimum", best.Value < 0.5, $"best {best.Value:F4} < 0.5");
            h.Check("All 400 trials recorded", study.Trials.Count == 400, $"got {study.Trials.Count}");
        }
        {
            var storage = new InMemoryStorage();
            var study = Study.Create(storage, new RandomSampler(seed: 7), StudyDirection.Maximize);
            study.Optimize(t =>
            {
                int n = t.SuggestInt("n", 0, 100);
                int n2 = t.SuggestInt("n", 0, 100);
                string mode = t.SuggestCategorical("mode", new[] { "a", "b", "c" });
                if (n != n2) throw new Exception("re-suggest not idempotent");
                return n + (mode == "b" ? 10.0 : 0.0);
            }, nTrials: 300);
            var best = study.BestTrial!;
            long bn = Convert.ToInt64(best.Params["n"]);
            string bmode = (string)best.Params["mode"];
            Console.WriteLine($"  best value={best.Value:F2} at n={bn}, mode={bmode}");
            h.Check("Maximize finds high n", bn >= 95, $"n={bn}");
            h.Check("Maximize prefers mode=b", bmode == "b", $"mode={bmode}");

            var s2 = Study.Create(storage, new RandomSampler(seed: 1), StudyDirection.Maximize);
            s2.Optimize(t =>
            {
                double x = t.SuggestFloat("x", 0, 1);
                if (x < 0.5) throw new InvalidOperationException("boom");
                return x;
            }, nTrials: 50);
            int complete = s2.GetTrials(new[] { TrialState.Complete }).Count;
            int failed = s2.GetTrials(new[] { TrialState.Fail }).Count;
            h.Check("Failed trials tolerated", complete + failed == 50 && failed > 0, $"complete={complete}, failed={failed}");
        }
    }

    private static void Stage2Grid(Harness h)
    {
        h.Section("Stage 2: GridSampler");
        var search = new List<KeyValuePair<string, IReadOnlyList<object>>>
        {
            new("flash_attn", new object[] { 0, 1 }),
            new("override", new object[] { "none", "all", "even", "odd" }),
        };
        var grid = new GridSampler(search);
        h.Check("GridSize = 2*4", grid.GridSize == 8, $"grid={grid.GridSize}");

        var storage = new InMemoryStorage();
        var study = Study.Create(storage, grid, StudyDirection.Maximize);
        var seen = new HashSet<string>();
        study.Optimize(t =>
        {
            int fa = t.SuggestCategorical("flash_attn", new[] { 0, 1 });
            string ov = t.SuggestCategorical("override", new[] { "none", "all", "even", "odd" });
            seen.Add($"{fa}|{ov}");
            return (fa == 1 ? 5.0 : 0.0) + (ov == "even" ? 3.0 : 0.0);
        }, nTrials: grid.GridSize);

        h.Check("All 8 grid cells visited exactly once", seen.Count == 8, $"distinct={seen.Count}");
        var best = study.BestTrial!;
        h.Check("Grid best = (1, even)", (int)best.Params["flash_attn"] == 1 && (string)best.Params["override"] == "even",
            $"fa={best.Params["flash_attn"]}, ov={best.Params["override"]}, value={best.Value}");
    }

    private static void Stage2TpeAcceptance(Harness h)
    {
        h.Section("Stage 2: TPESampler acceptance (must beat Random)");

        void AcceptanceTest(string name, Func<Trial, double> obj, int nTrials, int seeds)
        {
            int tpeWins = 0;
            double tpeSum = 0, rndSum = 0;
            for (int s = 0; s < seeds; s++)
            {
                double tpe = RunStudy(new TPESampler(seed: s, nStartupTrials: 10), obj, nTrials);
                double rnd = RunStudy(new RandomSampler(seed: 1000 + s), obj, nTrials);
                tpeSum += tpe;
                rndSum += rnd;
                if (tpe < rnd) tpeWins++;
            }
            double tpeMean = tpeSum / seeds, rndMean = rndSum / seeds;
            Console.WriteLine($"  [{name}] budget={nTrials}, seeds={seeds}: TPE mean best={tpeMean:F4}, Random mean best={rndMean:F4}, TPE wins {tpeWins}/{seeds}");
            h.Check($"TPE beats Random on '{name}' (mean)", tpeMean < rndMean, $"TPE {tpeMean:F4} < Random {rndMean:F4}");
            h.Check($"TPE wins majority of seeds on '{name}'", tpeWins * 2 > seeds, $"{tpeWins}/{seeds}");
        }

        AcceptanceTest("quadratic-2d", Quadratic2D, nTrials: 40, seeds: 12);
        AcceptanceTest("multimodal-1d", t =>
        {
            double x = t.SuggestFloat("x", -5.0, 5.0);
            return Math.Sin(3.0 * x) + 0.1 * (x - 1.0) * (x - 1.0);
        }, nTrials: 40, seeds: 12);
        AcceptanceTest("integer-1d", t =>
        {
            long n = t.SuggestInt("n", 0, 200);
            return (n - 37.0) * (n - 37.0);
        }, nTrials: 40, seeds: 12);
    }

    private static void Stage3CsvParser(Harness h)
    {
        h.Section("Stage 3: llama-bench CSV parser");
        string csv = string.Join("\n", new[]
        {
            "build_commit,model_filename,n_gen,n_prompt,avg_ts,stddev_ts",
            "abc123,model.gguf,128,0,73.521,1.204",
            "abc123,model.gguf,0,256,512.880,4.110",
        });
        var r = BenchCsvParser.Parse(csv);
        h.Check("CSV tg parsed", Math.Abs(r.TgTs - 73.521) < 1e-6, $"tg={r.TgTs}");
        h.Check("CSV tg stddev parsed", Math.Abs(r.TgStddev - 1.204) < 1e-6, $"tgStd={r.TgStddev}");
        h.Check("CSV pp parsed", Math.Abs(r.PpTs - 512.880) < 1e-6, $"pp={r.PpTs}");
        h.Check("CSV mean metric", Math.Abs(r.MetricValue(BenchMetric.Mean) - (73.521 + 512.880) / 2) < 1e-6,
            $"mean={r.MetricValue(BenchMetric.Mean)}");
        h.Check("CSV mean stddev quadrature",
            Math.Abs(r.MetricStddev(BenchMetric.Mean) - Math.Sqrt(1.204 * 1.204 + 4.110 * 4.110)) < 1e-6,
            $"meanStd={r.MetricStddev(BenchMetric.Mean)}");

        string quoted = string.Join("\n", new[]
        {
            "model_filename,test,n_gen,n_prompt,avg_ts,stddev_ts",
            "\"m,odel.gguf\",\"tg,128\",128,0,10.5,0.1",
        });
        var rq = BenchCsvParser.Parse(quoted);
        h.Check("CSV quoted comma handled", Math.Abs(rq.TgTs - 10.5) < 1e-6, $"tg={rq.TgTs}");

        var re = BenchCsvParser.Parse("");
        h.Check("CSV empty input safe", re.TgTs == 0 && re.PpTs == 0, $"tg={re.TgTs}, pp={re.PpTs}");
    }

    private static void Stage4CommandBuilders(Harness h)
    {
        h.Section("Stage 4: command builders");
        {
            string server = BenchCommandBuilder.BuildServer(
                "llama-server", "/models/m.gguf", threads: 8, batch: 4096, ubatch: 1024, ngl: 93,
                flash: true, overridePattern: @"blk\.\d+\.ffn_.*_exps\.=CPU", ctxSize: 8192);
            h.Check("server cmd core",
                server.Contains("--model /models/m.gguf") && server.Contains("-t 8") &&
                server.Contains("--batch-size 4096") && server.Contains("--ubatch-size 1024") && server.Contains("-ngl 93"),
                server);
            h.Check("server cmd flash on|off (not bare)", server.Contains("--flash-attn on"), server);
            h.Check("server cmd ctx-size", server.Contains("--ctx-size 8192"), server);
            h.Check("server cmd override quoted", server.Contains("--override-tensor \"blk\\.\\d+\\.ffn_.*_exps\\.=CPU\""), server);

            string serverOff = BenchCommandBuilder.BuildServer(
                "llama-server", "/m.gguf", 4, 512, 512, 0, flash: false, overridePattern: null, ctxSize: null);
            h.Check("server cmd flash off omitted", !serverOff.Contains("--flash-attn"), serverOff);
            h.Check("server cmd no override when null", !serverOff.Contains("--override-tensor"), serverOff);

            string serverFit = BenchCommandBuilder.BuildServer(
                "llama-server", "/m.gguf", 6, 4096, 1024, null, flash: true, overridePattern: null, ctxSize: 65535);
            h.Check("server cmd omits -ngl when null (fit)", !serverFit.Contains("-ngl") && serverFit.Contains("--ctx-size 65535"), serverFit);

            string serverMoe = BenchCommandBuilder.BuildServer(
                "llama-server", "/m.gguf", 7, 11410, 6698, 29, flash: true, overridePattern: null, ctxSize: 65535, nCpuMoe: 18);
            h.Check("server cmd --n-cpu-moe when tuned", serverMoe.Contains("--n-cpu-moe 18"), serverMoe);
            h.Check("server cmd no --n-cpu-moe when null", !serverFit.Contains("--n-cpu-moe"), serverFit);
        }
        {
            string benchInt = BenchCommandBuilder.BuildBench(
                "C:/llama/llama-bench.exe", "C:/my models/m.gguf", 8, 4096, 1024, 93,
                flash: true, overridePattern: null,
                flashStyle: FlashAttnStyle.Integer, supportsNoWarmup: true);
            h.Check("bench cmd flash integer", benchInt.Contains("--flash-attn 1"), benchInt);
            h.Check("bench cmd quotes spaced model path", benchInt.Contains("\"C:/my models/m.gguf\""), benchInt);
            h.Check("bench cmd -n/-p/-r stable", benchInt.Contains("-n 128") && benchInt.Contains("-p 256") && benchInt.Contains("-r 6"), benchInt);
            h.Check("bench cmd no-warmup when supported", benchInt.Contains("--no-warmup"), benchInt);

            string benchOnOff = BenchCommandBuilder.BuildBench(
                "llama-bench", "/m.gguf", 8, 4096, 1024, 93,
                flash: false, overridePattern: null,
                flashStyle: FlashAttnStyle.OnOff, supportsNoWarmup: false);
            h.Check("bench cmd flash on|off style", benchOnOff.Contains("--flash-attn off"), benchOnOff);
            h.Check("bench cmd no-warmup omitted when unsupported", !benchOnOff.Contains("--no-warmup"), benchOnOff);

            string benchFit = BenchCommandBuilder.BuildBench(
                "llama-bench", "/m.gguf", 6, 4096, 1024, null,
                flash: true, overridePattern: null,
                flashStyle: FlashAttnStyle.OnOff, supportsNoWarmup: true);
            h.Check("bench cmd omits -ngl when null (fit)", !benchFit.Contains("-ngl"), benchFit);

            string def = BenchCommandBuilder.BuildDefaultBench(
                "llama-bench", "/m.gguf", supportsNoWarmup: true);
            h.Check("default bench has no tuning flags",
                !def.Contains("--batch-size") && !def.Contains("-ngl") && def.Contains("-n 128") && def.Contains("--no-warmup"), def);
        }
    }

    private static void VariantBServerArgv(Harness h)
    {
        h.Section("Variant B (B1): server argv builder");
        var moe = new BenchArgs
        {
            ModelPath = "/models/m.gguf",
            Threads = 7, BatchSize = 11410, UBatchSize = 6698, GpuLayers = 29, NCpuMoe = 18,
            CtxSize = 65535, CacheTypeK = "q8_0", CacheTypeV = "q8_0", MmprojPath = "/models/mmproj.gguf",
            FlashAttn = false,
        };
        var argv = ServerArgvBuilder.Build(moe, 8085);
        string s = string.Join(" ", argv);
        h.Check("server-B argv core", s.Contains("--model /models/m.gguf") && s.Contains("--host 127.0.0.1") && s.Contains("--port 8085"), s);
        h.Check("server-B argv numerics", s.Contains("-c 65535") && s.Contains("-t 7") && s.Contains("-b 11410") && s.Contains("-ub 6698") && s.Contains("-ngl 29"), s);
        h.Check("server-B argv --n-cpu-moe", s.Contains("--n-cpu-moe 18"), s);
        h.Check("server-B argv cache types", s.Contains("-ctk q8_0") && s.Contains("-ctv q8_0"), s);
        h.Check("server-B argv mmproj", s.Contains("--mmproj /models/mmproj.gguf"), s);
        h.Check("server-B argv forces flash on for quantized KV", s.Contains("--flash-attn on"), s);
        h.Check("server-B argv flash is on|off not 0|1", !s.Contains("--flash-attn 0") && !s.Contains("--flash-attn 1"), s);

        var fit = moe with { UseFit = true, GpuLayers = 99, OverridePattern = @"blk\.\d+\.ffn_.*_exps\.=CPU", CacheTypeK = "f16", CacheTypeV = "f16", FlashAttn = true };
        string sf = string.Join(" ", ServerArgvBuilder.Build(fit, 9000));
        h.Check("server-B argv omits -ngl in fit mode", !sf.Contains("-ngl"), sf);
        h.Check("server-B argv override-tensor passthrough", sf.Contains("--override-tensor blk\\.\\d+\\.ffn_.*_exps\\.=CPU"), sf);
        h.Check("server-B argv flash on rendered (f16, flash=true)", sf.Contains("--flash-attn on"), sf);

        var noflash = new BenchArgs { ModelPath = "/m.gguf", FlashAttn = false };
        string sn = string.Join(" ", ServerArgvBuilder.Build(noflash, 1234));
        h.Check("server-B argv flash off when f16", sn.Contains("--flash-attn off"), sn);

        int port = ServerArgvBuilder.FindFreePort();
        h.Check("FindFreePort in range", port > 0 && port <= 65535, $"port={port}");
        h.Check("BaseUrl format", ServerArgvBuilder.BaseUrl(8085) == "http://127.0.0.1:8085", ServerArgvBuilder.BaseUrl(8085));
    }
}
