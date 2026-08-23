using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Models.Benchmarking;
using LlamaServerLauncher.Services.Optimization;

namespace LlamaServerLauncher.Services.Benchmarking;

public sealed class BenchmarkRunOptions
{
    public string ProfileName { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public ServerConfiguration Config { get; set; } = new();
    public bool RunInDocker { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string? HardwareSummary { get; set; }
    public string? LlamaVersion { get; set; }
    public bool RunStandardWorkload { get; set; }
    public bool StopAfterWorkload { get; set; }
    public int StdPromptTokens { get; set; } = 512;
    public int StdNPredict { get; set; } = 128;
    public int StdRepeat { get; set; } = 3;

    public bool RunPromptWorkload { get; set; }
    public string PromptSystem { get; set; } = string.Empty;
    public string PromptsText { get; set; } = string.Empty;
    public bool PromptKeepContext { get; set; } = true;
    public int PromptMaxTokens { get; set; }
    public int PromptTimeoutSeconds { get; set; } = 600;

    public bool HasWorkload => RunStandardWorkload || RunPromptWorkload;
}

public sealed class BenchmarkRunController : IDisposable
{
    private readonly BenchmarkStorageService _storage;
    private readonly LogService _log;
    private readonly HttpClient _metricsClient = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly HttpBenchmarkService _http;
    private readonly PromptRunService _promptRun;
    private readonly CancellationTokenSource _workloadCts = new();

    private ServerInstance? _instance;
    private BenchmarkRunOptions? _options;
    private DateTime _startedAt;
    private Timer? _timer;
    private string? _lastMetricsText;
    private int _tickBusy;
    private int _workloadState;
    private Task? _workloadTask;
    private double? _stdGen;
    private double? _stdPrompt;
    private double? _stdTtft;
    private PromptRunReport? _promptReport;
    private int _finished;

    public BenchmarkRunController(BenchmarkStorageService storage, LogService log)
    {
        _storage = storage;
        _log = log;
        _http = new HttpBenchmarkService(log: log);
        _promptRun = new PromptRunService(log);
    }

    public void Begin(ServerInstance instance, BenchmarkRunOptions options)
    {
        _instance = instance;
        _options = options;
        _startedAt = DateTime.Now;
        _timer = new Timer(OnTick, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
    }

    private async void OnTick(object? state)
    {
        if (Interlocked.Exchange(ref _tickBusy, 1) == 1)
            return;
        try
        {
            var instance = _instance;
            var options = _options;
            if (instance == null || options == null || !instance.IsRunning)
                return;

            await ScrapeMetricsAsync(instance);

            if (options.HasWorkload
                && instance.IsReady
                && Interlocked.CompareExchange(ref _workloadState, 1, 0) == 0)
            {
                _workloadTask = RunWorkloadAsync(instance, options);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _tickBusy, 0);
        }
    }

    private async Task ScrapeMetricsAsync(ServerInstance instance)
    {
        try
        {
            using var resp = await _metricsClient.GetAsync($"{instance.BaseUrl}/metrics");
            if (resp.IsSuccessStatusCode)
                _lastMetricsText = await resp.Content.ReadAsStringAsync();
        }
        catch
        {
        }
    }

    private async Task RunWorkloadAsync(ServerInstance instance, BenchmarkRunOptions options)
    {
        try
        {
            if (options.RunStandardWorkload)
                await RunStandardWorkloadAsync(instance, options);

            if (options.RunPromptWorkload && instance.IsRunning)
                await RunPromptWorkloadAsync(instance, options);
        }
        catch (OperationCanceledException)
        {
            _log.AppLog($"Benchmark workload for '{options.ProfileName}' was interrupted.");
        }
        catch (Exception ex)
        {
            _log.Error($"Benchmark workload failed: {ex.Message}");
        }
        finally
        {
            if (options.StopAfterWorkload && instance.IsRunning)
            {
                try { await instance.StopAsync(); } catch { }
            }
        }
    }

    private async Task RunStandardWorkloadAsync(ServerInstance instance, BenchmarkRunOptions options)
    {
        var prompt = BuildPrompt(options.StdPromptTokens);
        int reps = Math.Max(1, options.StdRepeat);
        double gen = 0, pp = 0, ttft = 0;
        int counted = 0;

        for (int i = 0; i < reps; i++)
        {
            if (!instance.IsRunning)
                break;
            var r = await _http.MeasureAsync(instance.BaseUrl, prompt, options.StdNPredict, 600, _workloadCts.Token);
            if (reps > 1 && i == 0)
                continue;
            gen += r.TgTs;
            pp += r.PpTs;
            ttft += r.TimeToFirstTokenMs;
            counted++;
        }

        if (counted > 0)
        {
            _stdGen = gen / counted;
            _stdPrompt = pp / counted;
            _stdTtft = ttft / counted;
            _log.AppLog($"Benchmark workload for '{options.ProfileName}': gen={_stdGen:F1} tok/s, prompt={_stdPrompt:F1} tok/s");
        }
    }

    private async Task RunPromptWorkloadAsync(ServerInstance instance, BenchmarkRunOptions options)
    {
        var report = new PromptRunReport
        {
            SystemPrompt = options.PromptSystem ?? string.Empty,
            KeepContext = options.PromptKeepContext,
            MaxTokens = options.PromptMaxTokens,
        };
        _promptReport = report;

        var promptOptions = new PromptRunOptions
        {
            SystemPrompt = report.SystemPrompt,
            PromptsText = options.PromptsText ?? string.Empty,
            KeepContext = options.PromptKeepContext,
            MaxTokens = options.PromptMaxTokens,
            TimeoutSeconds = options.PromptTimeoutSeconds,
            ApiKey = options.Config?.ApiKey,
            PreferredModel = PreferredModelName(options.Config),
        };

        await _promptRun.RunAsync(instance.BaseUrl, promptOptions, report, _workloadCts.Token);
        _log.AppLog($"Prompt run for '{options.ProfileName}' finished: {report.CompletedTurns} answered, {report.FailedTurns} failed.");
    }

    private static string? PreferredModelName(ServerConfiguration? config)
    {
        if (config == null)
            return null;
        if (!string.IsNullOrWhiteSpace(config.Alias))
            return config.Alias;
        if (!string.IsNullOrWhiteSpace(config.ModelPath))
            return System.IO.Path.GetFileNameWithoutExtension(config.ModelPath);
        if (!string.IsNullOrWhiteSpace(config.HfFile))
            return System.IO.Path.GetFileNameWithoutExtension(config.HfFile);
        return null;
    }

    private static string BuildPrompt(int approxTokens)
    {
        int words = Math.Max(1, approxTokens);
        var sb = new StringBuilder(words * 5);
        for (int i = 0; i < words; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append("word");
        }
        return sb.ToString();
    }

    public async Task<(BenchmarkRun Run, string Dir)?> FinishAndSaveAsync()
    {
        if (Interlocked.Exchange(ref _finished, 1) == 1)
            return null;

        var instance = _instance;
        var options = _options;
        if (instance == null || options == null)
            return null;

        _timer?.Change(Timeout.Infinite, Timeout.Infinite);

        if (_workloadTask != null)
        {
            try { _workloadCts.Cancel(); } catch { }
            try { await Task.WhenAny(_workloadTask, Task.Delay(TimeSpan.FromSeconds(30))); }
            catch { }
        }

        await ScrapeMetricsAsync(instance);

        var promptReport = _promptReport;
        if (promptReport != null && promptReport.Turns.Count == 0)
            promptReport = null;

        var metrics = new BenchmarkMetrics
        {
            DurationSeconds = Math.Max(0, (DateTime.Now - _startedAt).TotalSeconds),
            LogGenTps = instance.GenTps,
            LogPromptTps = instance.PromptTps,
            StdGenTps = _stdGen,
            StdPromptTps = _stdPrompt,
            StdTtftMs = _stdTtft,
            StdRepeat = options.RunStandardWorkload ? options.StdRepeat : null,
            StdPromptTokens = options.RunStandardWorkload ? options.StdPromptTokens : null,
            StdNPredict = options.RunStandardWorkload ? options.StdNPredict : null,
            PromptRunTurns = promptReport?.Turns.Count,
            PromptRunGenTps = promptReport?.AvgGenTps,
            PromptRunPromptTps = promptReport?.AvgPromptTps,
            PromptRunTtftMs = promptReport?.AvgTtftMs,
        };

        if (_lastMetricsText != null)
        {
            metrics.Prometheus = PrometheusMetricsParser.Parse(_lastMetricsText);
            PrometheusMetricsParser.ApplyKnown(metrics.Prometheus, metrics);
        }

        var run = new BenchmarkRun
        {
            Id = _startedAt.ToString("yyyy-MM-dd_HH-mm-ss"),
            ProfileName = options.ProfileName,
            CreatedAt = _startedAt,
            Label = options.Label,
            Notes = options.Notes,
            Command = options.Command,
            RunInDocker = options.RunInDocker,
            ConfigSnapshot = options.Config,
            Metrics = metrics,
            PromptRun = promptReport,
            HardwareSummary = options.HardwareSummary,
            LlamaVersion = options.LlamaVersion,
        };

        var report = BenchmarkReportBuilder.BuildRunReport(run, BenchmarkReportLocalizer.Localize);
        var promptRunMd = promptReport != null
            ? PromptRunDocument.BuildMarkdown(run, promptReport, BenchmarkReportLocalizer.Localize)
            : null;
        var log = instance.GetRunLogSnapshot();
        var dir = await _storage.SaveRunAsync(run, log, _lastMetricsText, report, promptRunMd);
        return (run, dir);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _metricsClient.Dispose();
        _workloadCts.Dispose();
    }
}
