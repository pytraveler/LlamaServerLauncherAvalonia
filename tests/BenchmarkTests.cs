using System;
using System.Collections.Generic;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Models.Benchmarking;
using LlamaServerLauncher.Services.Benchmarking;

public static class BenchmarkTests
{
    public static void Run(Harness h)
    {
        h.Section("PrometheusMetricsParser.Parse");

        var text = string.Join("\n", new[]
        {
            "# HELP llamacpp:prompt_tokens_total Number of prompt tokens processed.",
            "# TYPE llamacpp:prompt_tokens_total counter",
            "llamacpp:prompt_tokens_total 1234",
            "# TYPE llamacpp:predicted_tokens_seconds gauge",
            "llamacpp:predicted_tokens_seconds 42.5",
            "llamacpp:kv_cache_usage_ratio 0.05",
            "llamacpp:requests_processing{some=\"label\"} 2",
            "llamacpp:tokens_predicted_total 900 1700000000000",
            "llamacpp:bad_value +Inf",
            ""
        });

        var m = PrometheusMetricsParser.Parse(text);
        h.Check("counter parsed", m.TryGetValue("llamacpp:prompt_tokens_total", out var pt) && pt == 1234, pt.ToString());
        h.Check("gauge parsed", m.TryGetValue("llamacpp:predicted_tokens_seconds", out var ps) && Math.Abs(ps - 42.5) < 1e-9, ps.ToString());
        h.Check("labeled metric value after brace", m.TryGetValue("llamacpp:requests_processing", out var rp) && rp == 2, rp.ToString());
        h.Check("trailing timestamp ignored", m.TryGetValue("llamacpp:tokens_predicted_total", out var tp) && tp == 900, tp.ToString());
        h.Check("infinity skipped", !m.ContainsKey("llamacpp:bad_value"), "ok");
        h.Check("comment lines skipped", !m.ContainsKey("#"), "ok");

        var metrics = new BenchmarkMetrics { Prometheus = m };
        PrometheusMetricsParser.ApplyKnown(m, metrics);
        h.Check("ApplyKnown predicted", metrics.PredictedTokensSeconds.HasValue && Math.Abs(metrics.PredictedTokensSeconds!.Value - 42.5) < 1e-9, "ok");
        h.Check("ApplyKnown kv ratio", metrics.KvCacheUsageRatio.HasValue && Math.Abs(metrics.KvCacheUsageRatio!.Value - 0.05) < 1e-9, "ok");
        h.Check("ApplyKnown prompt total", metrics.PromptTokensTotal == 1234, "ok");
        h.Check("ApplyKnown predicted total", metrics.TokensPredictedTotal == 900, "ok");

        h.Check("empty text yields empty dict", PrometheusMetricsParser.Parse("").Count == 0, "ok");
        h.Check("null text yields empty dict", PrometheusMetricsParser.Parse(null).Count == 0, "ok");

        h.Section("BenchmarkReportBuilder.BuildComparison");

        var run1 = new BenchmarkRun
        {
            ProfileName = "qwen",
            Id = "2026-01-01_10-00-00",
            Label = "A",
            CreatedAt = new DateTime(2026, 1, 1, 10, 0, 0),
            LlamaVersion = "b4123",
            HardwareSummary = "RTX 4090 24GB",
            Metrics = new BenchmarkMetrics
            {
                StdGenTps = 42.1,
                Prometheus = new Dictionary<string, double> { ["llamacpp:kv_cache_tokens"] = 512 }
            },
            ConfigSnapshot = new ServerConfiguration { ContextSize = 4096, GpuLayers = 99, Seed = 42 }
        };
        var run2 = new BenchmarkRun
        {
            ProfileName = "qwen",
            Id = "2026-01-01_11-00-00",
            Label = "B|risky",
            CreatedAt = new DateTime(2026, 1, 1, 11, 0, 0),
            Metrics = new BenchmarkMetrics { LogGenTps = 27.4 },
            ConfigSnapshot = new ServerConfiguration { ContextSize = 8192, GpuLayers = 50 }
        };

        var md = BenchmarkReportBuilder.BuildComparison(new List<BenchmarkRun> { run1, run2 });
        h.Check("has header row", md.Contains("| Metric |"), "ok");
        h.Check("has separator row", md.Contains("| --- |"), "ok");
        h.Check("has gen tps row", md.Contains("Gen tok/s"), "ok");
        h.Check("formats run1 gen tps", md.Contains("42.10"), "ok");
        h.Check("formats run2 gen tps", md.Contains("27.40"), "ok");
        h.Check("run header A", md.Contains("qwen / A"), "ok");
        h.Check("escapes pipe in label", md.Contains("B\\|risky"), "ok");
        h.Check("shows ngl", md.Contains("| ngl |"), "ok");
        h.Check("shows seed", md.Contains("| seed |") && md.Contains("| 42 |"), "ok");
        h.Check("shows llama.cpp version", md.Contains("| llama.cpp |") && md.Contains("b4123"), "ok");
        h.Check("shows hardware", md.Contains("| Hardware |") && md.Contains("RTX 4090 24GB"), "ok");
        h.Check("shows prometheus extras", md.Contains("| kv_cache_tokens |") && md.Contains("| 512 |"), "ok");
        h.Check("shows mmap/parallel rows", md.Contains("| mmap |") && md.Contains("| parallel |"), "ok");

        var mdLoc = BenchmarkReportBuilder.BuildComparison(
            new List<BenchmarkRun> { run1 }, s => s == "Metric" ? "M17" : s);
        h.Check("localizer applied to header", mdLoc.Contains("| M17 |"), "ok");
        h.Check("localizer passthrough", mdLoc.Contains("| ngl |"), "ok");

        var empty = BenchmarkReportBuilder.BuildComparison(new List<BenchmarkRun>());
        h.Check("empty comparison message", empty.Contains("No benchmarks selected"), "ok");

        h.Section("BenchmarkReportBuilder.BuildRunReport");

        run1.Command = "\"llama-server\" -m model.gguf -c 4096";
        var report = BenchmarkReportBuilder.BuildRunReport(run1);
        h.Check("report title", report.Contains("# Benchmark:"), "ok");
        h.Check("report metric table", report.Contains("| Metric | Value |"), "ok");
        h.Check("report command fence", report.Contains("```") && report.Contains("-c 4096"), "ok");
    }
}
