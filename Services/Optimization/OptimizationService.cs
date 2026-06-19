using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LlamaServerLauncher.Models.Optimization;
using LlamaServerLauncher.Resources;
using LlamaServerLauncher.Optimization.Samplers;
using LlamaServerLauncher.Optimization.Storage;
using LlamaServerLauncher.Optimization.Study;
using LlamaServerLauncher.Optimization.Trial;

namespace LlamaServerLauncher.Services.Optimization;

public sealed class OptimizationService
{
    private const string PBatch = "batch";
    private const string PUBatch = "ubatch";
    private const string PThreads = "threads";
    private const string PGpuLayers = "gpu_layers";
    private const string PNCpuMoe = "n_cpu_moe";
    private const string PFlashAttn = "flash_attn";
    private const string POverride = "override_tensor";

    private const int CompareNGen = BenchCommandBuilder.CompareNGen;
    private const int CompareNPrompt = BenchCommandBuilder.CompareNPrompt;
    private const int CompareRepeat = BenchCommandBuilder.CompareRepeat;

    private readonly LogService? _log;

    public OptimizationService(LogService? log = null)
    {
        _log = log;
    }

    public async Task<OptimizationResult> RunAsync(
        OptimizationOptions opts,
        IProgress<OptimizationProgress>? progress,
        CancellationToken ct)
    {
        bool http = opts.UseHttpBenchmark;

        IConfigBenchmarker bench;
        LlamaBenchService? llamaBench = null;
        LlamaBenchCapabilities caps = LlamaBenchCapabilities.Default;
        string? benchPath = null;

        if (http)
        {
            var serverExe = LlamaServerService.ResolveExecutablePath(
                string.IsNullOrWhiteSpace(opts.LlamaServerExecutablePath) ? "llama-server" : opts.LlamaServerExecutablePath!);
            if (serverExe == null)
                throw new InvalidOperationException(LocalizedStrings.Instance.OptErrServerNotFound);
            bench = new ServerBenchmarkService(serverExe, log: _log, noMmap: opts.NoMmap);
            _log?.Info($"OptimizationService: HTTP benchmark via real llama-server at {serverExe} (noMmap={opts.NoMmap})");
        }
        else
        {
            benchPath = LlamaBenchService.ResolveBenchPath(opts.LlamaBenchPath, opts.LlamaServerExecutablePath);
            if (benchPath == null)
                throw new InvalidOperationException(LocalizedStrings.Instance.OptErrBenchNotFound);
            caps = await LlamaBenchCapabilities.DetectAsync(benchPath);
            llamaBench = new LlamaBenchService(benchPath, caps, _log);
            bench = llamaBench;
            _log?.Info($"OptimizationService: using llama-bench at {benchPath} (flashAttn={caps.FlashAttn}, noWarmup={caps.SupportsNoWarmup}, fit={caps.SupportsFit})");
        }

        bool fitMode = !http && opts.UseFit && caps.SupportsFit && opts.ContextSize is { };
        bool fitOwnsNgl = http ? opts.UseFit : fitMode;

        if (!http)
        {
            if (opts.UseFit && !fitMode)
                progress?.Report(new OptimizationProgress { Phase = OptimizationPhase.NglEstimation,
                    Message = caps.SupportsFit
                        ? LocalizedStrings.Instance.OptWarnFitNoCtx
                        : LocalizedStrings.Instance.OptWarnNoFitFlag });

            if (opts.ContextSize is { } && !caps.SupportsCtxSize && !fitMode)
                progress?.Report(new OptimizationProgress { Phase = OptimizationPhase.NglEstimation,
                    Message = LocalizedStrings.Instance.OptWarnNoCtxFlag });
            if ((!string.IsNullOrWhiteSpace(opts.CacheTypeK) || !string.IsNullOrWhiteSpace(opts.CacheTypeV)) && !caps.SupportsCacheType)
                progress?.Report(new OptimizationProgress { Phase = OptimizationPhase.NglEstimation,
                    Message = LocalizedStrings.Instance.OptWarnNoCacheFlag });
        }

        int threadsHigh = opts.SearchSpace.Threads.High;
        var (nGen, nPrompt) = TokensFor(opts.Metric, opts.NTokens);

        var runtimeBase = new BenchArgs
        {
            ModelPath = opts.ModelPath,
            CtxSize = opts.ContextSize,
            CacheTypeK = opts.CacheTypeK,
            CacheTypeV = opts.CacheTypeV,
            MmprojPath = opts.MmprojPath,
            UseFit = http ? opts.UseFit : fitMode,
            FitMarginMiB = opts.FitMarginMiB,
        };

        int nglMax;
        if (fitOwnsNgl)
        {
            nglMax = 0;
        }
        else if (opts.NglMax is { } given)
        {
            nglMax = given;
        }
        else if (http)
        {
            nglMax = opts.SearchSpace.GpuLayers.High;
        }
        else
        {
            progress?.Report(new OptimizationProgress { Phase = OptimizationPhase.NglEstimation, Message = LocalizedStrings.Instance.OptProgEstimatingNgl });
            var nglEstimator = new NglEstimator(llamaBench!, _log);
            var nglProbe = runtimeBase with { Threads = threadsHigh, NGen = 1, NPrompt = 1, Repeat = 1 };
            nglMax = await nglEstimator.EstimateMaxAsync(
                nglProbe, opts.SearchSpace.GpuLayers.High, opts.NglTimeoutSeconds,
                new Progress<int>(n => progress?.Report(new OptimizationProgress
                {
                    Phase = OptimizationPhase.NglEstimation,
                    Message = string.Format(LocalizedStrings.Instance.OptProgProbingNgl, n)
                })),
                ct);
        }
        var ss = fitOwnsNgl ? opts.SearchSpace : opts.SearchSpace.WithGpuLayersHigh(nglMax);

        if (!http && !opts.NoWarmup)
        {
            progress?.Report(new OptimizationProgress { Phase = OptimizationPhase.Warmup, Message = LocalizedStrings.Instance.OptProgWarmup });
            var warmup = new WarmupService(llamaBench!, _log);
            await warmup.WarmupAsync(
                runtimeBase, nglMax, threadsHigh, opts.Metric, opts.NWarmupRuns, opts.NWarmupTokens, opts.BenchTimeoutSeconds,
                new Progress<WarmupProgress>(w => progress?.Report(new OptimizationProgress
                {
                    Phase = OptimizationPhase.Warmup,
                    Message = string.Format(LocalizedStrings.Instance.OptProgWarmupResult, w.Run, w.TotalRuns, w.TokensPerSec.ToString("F1", CultureInfo.InvariantCulture))
                })),
                ct);
        }

        var storage = new InMemoryStorage();

        var study1 = Study.Create(storage, new TPESampler(), StudyDirection.Maximize, studyName: "stage1");
        await RunNumericStageAsync(study1, 1, opts.NTrials, ss, opts, bench, runtimeBase, nGen, nPrompt,
            fixedFlash: null, fixedOverridePattern: null, fitOwnsNgl, progress, ct);
        var best1 = RequireBest(study1, "Stage 1");

        int b1Batch = GetInt(best1, PBatch);
        int b1UBatch = GetInt(best1, PUBatch);
        int b1Threads = GetInt(best1, PThreads);
        int? b1Ngl = fitOwnsNgl ? null : GetInt(best1, PGpuLayers);
        int? b1NCpuMoe = opts.TuneNCpuMoe ? GetInt(best1, PNCpuMoe) : (int?)null;

        bool scan = opts.OverrideMode == OverrideMode.Scan;
        bool kvQuantized = IsQuantizedCache(opts.CacheTypeK) || IsQuantizedCache(opts.CacheTypeV);
        var flashChoices = kvQuantized ? new object[] { true } : new object[] { false, true };
        var overrideKeys = scan ? OverridePatterns.Keys : new[] { "none" };

        var gridSpace = new List<KeyValuePair<string, IReadOnlyList<object>>>
        {
            new(PFlashAttn, flashChoices),
        };
        if (scan)
            gridSpace.Add(new(POverride, overrideKeys.Cast<object>().ToList()));

        var grid = new GridSampler(gridSpace);
        var study2 = Study.Create(storage, grid, StudyDirection.Maximize, studyName: "stage2");
        await RunCategoricalStageAsync(study2, grid.GridSize, scan, opts, bench, runtimeBase, nGen, nPrompt,
            b1Batch, b1UBatch, b1Threads, b1Ngl, b1NCpuMoe, progress, ct);
        var best2 = RequireBest(study2, "Stage 2");

        bool best2Flash = GetBool(best2, PFlashAttn);
        string best2OverrideKey = best2.Params.TryGetValue(POverride, out var ovk) ? (string)ovk : "none";
        string best2OverridePattern = OverridePatterns.PatternFor(best2OverrideKey);

        var study3 = Study.Create(storage, new TPESampler(), StudyDirection.Maximize, studyName: "stage3");
        await RunNumericStageAsync(study3, 3, opts.NTrials, ss, opts, bench, runtimeBase, nGen, nPrompt,
            fixedFlash: best2Flash, fixedOverridePattern: best2OverridePattern, fitOwnsNgl, progress, ct);
        var best3 = RequireBest(study3, "Stage 3");

        int finalBatch = GetInt(best3, PBatch);
        int finalUBatch = GetInt(best3, PUBatch);
        int finalThreads = GetInt(best3, PThreads);
        int? finalNgl = fitOwnsNgl ? null : GetInt(best3, PGpuLayers);
        int? finalNCpuMoe = opts.TuneNCpuMoe ? GetInt(best3, PNCpuMoe) : (int?)null;
        double bestValue = best3.Value ?? 0.0;

        progress?.Report(new OptimizationProgress { Phase = OptimizationPhase.Comparison, Message = LocalizedStrings.Instance.OptProgComparison });
        var optimizedArgs = runtimeBase with
        {
            Threads = finalThreads,
            BatchSize = finalBatch,
            UBatchSize = finalUBatch,
            GpuLayers = finalNgl,
            NCpuMoe = finalNCpuMoe,
            FlashAttn = best2Flash,
            OverridePattern = best2OverridePattern,
            Repeat = CompareRepeat,
            NGen = CompareNGen,
            NPrompt = CompareNPrompt,
        };
        var optResult = await TryComparisonResultAsync(bench, optimizedArgs, opts, ct);
        double? optimizedValue = optResult?.MetricValue(opts.Metric);
        if (fitMode && optResult?.GpuLayers is { } fittedNgl && fittedNgl >= 0)
            finalNgl = fittedNgl;

        var defaultArgs = runtimeBase with
        {
            Repeat = CompareRepeat,
            NGen = CompareNGen,
            NPrompt = CompareNPrompt,
        };
        var defResult = await TryComparisonResultAsync(bench, defaultArgs, opts, ct);
        double? defaultValue = defResult?.MetricValue(opts.Metric);

        double? improvementPct = (optimizedValue is { } o && defaultValue is { } d && d > 0)
            ? (o - d) / d * 100.0
            : null;

        bool improvedOverBaseline = !(optimizedValue is { } ov && defaultValue is { } dv && ov <= dv);

        int? recommendedCtx = null;
        if (!http && opts.EstimateContext)
        {
            progress?.Report(new OptimizationProgress { Phase = OptimizationPhase.ContextEstimation, Message = LocalizedStrings.Instance.OptProgEstimatingCtx });
            var ctxEstimator = new ContextEstimator(llamaBench!, _log);
            var ctxProbe = runtimeBase with
            {
                GpuLayers = finalNgl,
                BatchSize = finalBatch,
                UBatchSize = finalUBatch,
                FlashAttn = best2Flash,
                NGen = 1,
                NPrompt = 1,
                Repeat = 1,
            };
            recommendedCtx = await ctxEstimator.EstimateMaxAsync(
                ctxProbe,
                progress: new Progress<int>(c => progress?.Report(new OptimizationProgress
                {
                    Phase = OptimizationPhase.ContextEstimation,
                    Message = string.Format(LocalizedStrings.Instance.OptProgProbingCtx, c)
                })),
                ct: ct);
        }

        string serverExeForCmd = string.IsNullOrWhiteSpace(opts.LlamaServerExecutablePath)
            ? "llama-server"
            : opts.LlamaServerExecutablePath!;
        string serverCmd = BenchCommandBuilder.BuildServer(serverExeForCmd, opts.ModelPath, finalThreads, finalBatch, finalUBatch, finalNgl,
            best2Flash, best2OverridePattern, recommendedCtx, finalNCpuMoe);

        string optimizedBenchCmd, defaultBenchCmd;
        if (http)
        {
            optimizedBenchCmd = bench.Describe(optimizedArgs);
            defaultBenchCmd = bench.Describe(defaultArgs);
        }
        else
        {
            optimizedBenchCmd = BenchCommandBuilder.BuildBench(benchPath!, opts.ModelPath, finalThreads, finalBatch, finalUBatch, finalNgl,
                best2Flash, best2OverridePattern, caps.FlashAttn, caps.SupportsNoWarmup, finalNCpuMoe);
            defaultBenchCmd = BenchCommandBuilder.BuildDefaultBench(benchPath!, opts.ModelPath, caps.SupportsNoWarmup);
        }

        progress?.Report(new OptimizationProgress { Phase = OptimizationPhase.Done, Message = LocalizedStrings.Instance.OptDone });

        return new OptimizationResult
        {
            Metric = opts.Metric,
            Threads = finalThreads,
            BatchSize = finalBatch,
            UBatchSize = finalUBatch,
            GpuLayers = finalNgl,
            NCpuMoe = finalNCpuMoe,
            FlashAttn = best2Flash,
            OverrideKey = best2OverrideKey == "none" ? null : best2OverrideKey,
            OverridePattern = string.IsNullOrEmpty(best2OverridePattern) ? null : best2OverridePattern,
            BestValue = bestValue,
            RecommendedCtxSize = recommendedCtx,
            OptimizedServerCmd = serverCmd,
            OptimizedBenchCmd = optimizedBenchCmd,
            DefaultBenchCmd = defaultBenchCmd,
            OptimizedBenchValue = optimizedValue,
            DefaultBenchValue = defaultValue,
            ImprovementPct = improvementPct,
            ImprovedOverBaseline = improvedOverBaseline,
        };
    }


    private async Task RunNumericStageAsync(
        Study study, int stageNum, int nTrials, SearchSpace ss, OptimizationOptions opts,
        IConfigBenchmarker bench, BenchArgs runtime, int nGen, int nPrompt,
        bool? fixedFlash, string? fixedOverridePattern, bool fitOwnsNgl,
        IProgress<OptimizationProgress>? progress, CancellationToken ct)
    {
        var phase = stageNum == 1 ? OptimizationPhase.Stage1 : OptimizationPhase.Stage3;
        double bestSoFar = double.NegativeInfinity;

        await study.OptimizeAsync(async trial =>
        {
            int batch = trial.SuggestInt(PBatch, ss.Batch.Low, ss.Batch.High);
            int ubatch = trial.SuggestInt(PUBatch, ss.UBatch.Low, ss.UBatch.High);
            int threads = trial.SuggestInt(PThreads, ss.Threads.Low, ss.Threads.High);
            int? ngl = fitOwnsNgl ? null : trial.SuggestInt(PGpuLayers, ss.GpuLayers.Low, ss.GpuLayers.High);
            int? ncpuMoe = opts.TuneNCpuMoe ? trial.SuggestInt(PNCpuMoe, ss.NCpuMoe.Low, ss.NCpuMoe.High) : (int?)null;

            if (opts.PruneBatchLtUbatch && batch < ubatch)
                throw new TrialPruned();

            var args = runtime with
            {
                BatchSize = batch,
                UBatchSize = ubatch,
                Threads = threads,
                GpuLayers = ngl,
                NCpuMoe = ncpuMoe,
                FlashAttn = fixedFlash,
                OverridePattern = fixedOverridePattern,
                Repeat = opts.Repeat,
                NGen = nGen,
                NPrompt = nPrompt,
            };

            progress?.Report(new OptimizationProgress { Phase = phase, Message = string.Format(LocalizedStrings.Instance.OptProgBenchmarking, stageNum, trial.Number + 1, nTrials) });
            double value = await RunBenchSafeAsync(bench, args, opts.BenchTimeoutSeconds, ct);
            bool isBest = value > bestSoFar;
            if (isBest) bestSoFar = value;
            progress?.Report(new OptimizationProgress
            {
                Phase = phase,
                Stage = stageNum,
                TrialNumber = trial.Number + 1,
                TrialsInStage = nTrials,
                Command = bench.Describe(args),
                Value = value,
                IsBest = isBest,
            });
            return value;
        }, nTrials, ct);
    }

    private async Task RunCategoricalStageAsync(
        Study study, int gridSize, bool scan, OptimizationOptions opts,
        IConfigBenchmarker bench, BenchArgs runtime, int nGen, int nPrompt,
        int batch, int ubatch, int threads, int? ngl, int? ncpuMoe,
        IProgress<OptimizationProgress>? progress, CancellationToken ct)
    {
        double bestSoFar = double.NegativeInfinity;

        await study.OptimizeAsync(async trial =>
        {
            bool flash = trial.SuggestCategorical(PFlashAttn, new[] { false, true });
            string overrideKey = scan
                ? trial.SuggestCategorical(POverride, OverridePatterns.Keys.ToList())
                : "none";
            string overridePattern = OverridePatterns.PatternFor(overrideKey);

            var args = runtime with
            {
                BatchSize = batch,
                UBatchSize = ubatch,
                Threads = threads,
                GpuLayers = ngl,
                NCpuMoe = ncpuMoe,
                FlashAttn = flash,
                OverridePattern = overridePattern,
                Repeat = opts.Repeat,
                NGen = nGen,
                NPrompt = nPrompt,
            };

            progress?.Report(new OptimizationProgress { Phase = OptimizationPhase.Stage2, Message = string.Format(LocalizedStrings.Instance.OptProgBenchmarking, 2, trial.Number + 1, gridSize) });
            double value = await RunBenchSafeAsync(bench, args, opts.BenchTimeoutSeconds, ct);
            bool isBest = value > bestSoFar;
            if (isBest) bestSoFar = value;
            progress?.Report(new OptimizationProgress
            {
                Phase = OptimizationPhase.Stage2,
                Stage = 2,
                TrialNumber = trial.Number + 1,
                TrialsInStage = gridSize,
                Command = bench.Describe(args),
                Value = value,
                IsBest = isBest,
            });
            return value;
        }, gridSize, ct);
    }

    private async Task<double> RunBenchSafeAsync(IConfigBenchmarker bench, BenchArgs args, int timeout, CancellationToken ct)
    {
        try
        {
            var result = await bench.RunBenchAsync(args, timeout, ct);
            return result.MetricValue(MetricOf(args));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log?.Warning($"Bench run failed (treated as 0.0): {ex.Message}");
            return 0.0;
        }
    }

    private async Task<BenchmarkResult?> TryComparisonResultAsync(IConfigBenchmarker bench, BenchArgs args, OptimizationOptions opts, CancellationToken ct)
    {
        try
        {
            return await bench.RunBenchAsync(args, opts.BenchTimeoutSeconds, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log?.Warning($"Comparison bench failed: {ex.Message}");
            return null;
        }
    }

    private static BenchMetric MetricOf(BenchArgs a) =>
        a.NGen > 0 && a.NPrompt > 0 ? BenchMetric.Mean :
        a.NPrompt > 0 ? BenchMetric.Pp : BenchMetric.Tg;


    private static (int nGen, int nPrompt) TokensFor(BenchMetric metric, int nTokens) => metric switch
    {
        BenchMetric.Tg => (nTokens, 0),
        BenchMetric.Pp => (0, 2 * nTokens),
        BenchMetric.Mean => (nTokens, 2 * nTokens),
        _ => (nTokens, 0),
    };

    private static FrozenTrial RequireBest(Study study, string stageName)
    {
        var best = study.BestTrial;
        if (best == null)
            throw new InvalidOperationException($"{stageName} produced no completed trials.");
        return best;
    }

    private static int GetInt(FrozenTrial t, string name) => Convert.ToInt32(t.Params[name], CultureInfo.InvariantCulture);

    private static bool GetBool(FrozenTrial t, string name) => Convert.ToBoolean(t.Params[name]);

    private static bool IsQuantizedCache(string? type) =>
        !string.IsNullOrWhiteSpace(type)
        && !type.Equals("f16", StringComparison.OrdinalIgnoreCase)
        && !type.Equals("f32", StringComparison.OrdinalIgnoreCase);
}
