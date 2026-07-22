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
}

public sealed class BenchmarkRunController : IDisposable
{
    private readonly BenchmarkStorageService _storage;
    private readonly LogService _log;
    private readonly HttpClient _metricsClient = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly HttpBenchmarkService _http;

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
    private int _finished;

    public BenchmarkRunController(BenchmarkStorageService storage, LogService log)
    {
        _storage = storage;
        _log = log;
        _http = new HttpBenchmarkService(log: log);
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

            if (options.RunStandardWorkload
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
            var prompt = BuildPrompt(options.StdPromptTokens);
            int reps = Math.Max(1, options.StdRepeat);
            double gen = 0, pp = 0, ttft = 0;
            int counted = 0;

            for (int i = 0; i < reps; i++)
            {
                if (!instance.IsRunning)
                    break;
                var r = await _http.MeasureAsync(instance.BaseUrl, prompt, options.StdNPredict, 600, CancellationToken.None);
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
        catch (Exception ex)
        {
            _log.Error($"Benchmark standard workload failed: {ex.Message}");
        }
        finally
        {
            if (options.StopAfterWorkload && instance.IsRunning)
            {
                try { await instance.StopAsync(); } catch { }
            }
        }
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
            try { await Task.WhenAny(_workloadTask, Task.Delay(TimeSpan.FromSeconds(30))); }
            catch { }
        }

        await ScrapeMetricsAsync(instance);

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
            HardwareSummary = options.HardwareSummary,
            LlamaVersion = options.LlamaVersion,
        };

        var report = BenchmarkReportBuilder.BuildRunReport(run, BenchmarkReportLocalizer.Localize);
        var log = instance.GetRunLogSnapshot();
        var dir = await _storage.SaveRunAsync(run, log, _lastMetricsText, report);
        return (run, dir);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _metricsClient.Dispose();
    }
}
