using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LlamaServerLauncher.Models.Benchmarking;

namespace LlamaServerLauncher.Services.Benchmarking;

public static class BenchmarkReportBuilder
{
    private static readonly (string Label, Func<BenchmarkRun, string> Value)[] Rows =
    {
        ("Profile", r => r.ProfileName),
        ("Date", r => r.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
        ("Model", r => ModelName(r)),
        ("Gen tok/s", r => Num(r.Metrics.StdGenTps ?? r.Metrics.LogGenTps ?? r.Metrics.PredictedTokensSeconds)),
        ("Prompt tok/s", r => Num(r.Metrics.StdPromptTps ?? r.Metrics.LogPromptTps ?? r.Metrics.PromptTokensSeconds)),
        ("TTFT, ms", r => Num(r.Metrics.StdTtftMs)),
        ("predicted_tokens_seconds", r => Num(r.Metrics.PredictedTokensSeconds)),
        ("prompt_tokens_seconds", r => Num(r.Metrics.PromptTokensSeconds)),
        ("kv_cache_usage_ratio", r => Num(r.Metrics.KvCacheUsageRatio, 4)),
        ("kv_cache_tokens", r => Prom(r, "llamacpp:kv_cache_tokens", 0)),
        ("tokens_predicted_total", r => Num(r.Metrics.TokensPredictedTotal, 0)),
        ("prompt_tokens_total", r => Num(r.Metrics.PromptTokensTotal, 0)),
        ("n_decode_total", r => Prom(r, "llamacpp:n_decode_total", 0)),
        ("n_busy_slots_per_decode", r => Prom(r, "llamacpp:n_busy_slots_per_decode")),
        ("requests_processing", r => Prom(r, "llamacpp:requests_processing", 0)),
        ("requests_deferred", r => Prom(r, "llamacpp:requests_deferred", 0)),
        ("Duration, s", r => Num(r.Metrics.DurationSeconds, 1)),
        ("ngl", r => Str(r.ConfigSnapshot.GpuLayers)),
        ("ctx", r => Str(r.ConfigSnapshot.ContextSize)),
        ("batch", r => Str(r.ConfigSnapshot.BatchSize)),
        ("ubatch", r => Str(r.ConfigSnapshot.UBatchSize)),
        ("threads", r => Str(r.ConfigSnapshot.Threads)),
        ("flash-attn", r => Flag(r.ConfigSnapshot.FlashAttention)),
        ("n-cpu-moe", r => Str(r.ConfigSnapshot.CpuMoe)),
        ("cache K/V", r => CacheTypes(r)),
        ("seed", r => Str(r.ConfigSnapshot.Seed)),
        ("temp", r => Num(r.ConfigSnapshot.Temperature)),
        ("parallel", r => Str(r.ConfigSnapshot.ParallelSlots)),
        ("mmap", r => Flag(r.ConfigSnapshot.Mmap)),
        ("mlock", r => Flag(r.ConfigSnapshot.Mlock)),
        ("mmproj", r => FileNameOr(r.ConfigSnapshot.MmprojPath)),
        ("Draft model", r => FileNameOr(r.ConfigSnapshot.SpecDraftModel)),
        ("Custom args", r => string.IsNullOrWhiteSpace(r.ConfigSnapshot.CustomArguments) ? "—" : r.ConfigSnapshot.CustomArguments.Trim()),
        ("docker", r => r.RunInDocker ? "yes" : "no"),
        ("llama.cpp", r => string.IsNullOrWhiteSpace(r.LlamaVersion) ? "—" : r.LlamaVersion!),
        ("Hardware", r => string.IsNullOrWhiteSpace(r.HardwareSummary) ? "—" : r.HardwareSummary!),
    };

    public static string BuildComparison(IReadOnlyList<BenchmarkRun> runs, Func<string, string>? localize = null)
    {
        string L(string s) => localize?.Invoke(s) ?? s;

        var sb = new StringBuilder();
        sb.Append("# ").AppendLine(L("Benchmark comparison"));
        sb.AppendLine();

        if (runs == null || runs.Count == 0)
        {
            sb.Append('_').Append(L("No benchmarks selected.")).AppendLine("_");
            return sb.ToString();
        }

        sb.Append("| ").Append(Cell(L("Metric"))).Append(" |");
        foreach (var run in runs)
            sb.Append(' ').Append(Cell(RunHeader(run))).Append(" |");
        sb.AppendLine();

        sb.Append("| --- |");
        foreach (var _ in runs)
            sb.Append(" --- |");
        sb.AppendLine();

        foreach (var (label, value) in Rows)
        {
            sb.Append("| ").Append(Cell(L(label))).Append(" |");
            foreach (var run in runs)
                sb.Append(' ').Append(Cell(value(run))).Append(" |");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public static string BuildRunReport(BenchmarkRun run, Func<string, string>? localize = null)
    {
        string L(string s) => localize?.Invoke(s) ?? s;

        var sb = new StringBuilder();
        sb.Append("# ").Append(L("Benchmark")).Append(": ").AppendLine(run.DisplayName);
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(run.Notes))
        {
            sb.AppendLine(run.Notes);
            sb.AppendLine();
        }

        sb.Append("| ").Append(Cell(L("Metric"))).Append(" | ").Append(Cell(L("Value"))).AppendLine(" |");
        sb.AppendLine("| --- | --- |");
        foreach (var (label, value) in Rows)
            sb.Append("| ").Append(Cell(L(label))).Append(" | ").Append(Cell(value(run))).AppendLine(" |");

        sb.AppendLine();
        sb.Append("## ").AppendLine(L("Command"));
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine(run.Command);
        sb.AppendLine("```");

        return sb.ToString();
    }

    private static string RunHeader(BenchmarkRun run)
    {
        var name = string.IsNullOrWhiteSpace(run.Label) ? run.Id : run.Label;
        return $"{run.ProfileName} / {name}";
    }

    private static string ModelName(BenchmarkRun run)
    {
        var path = run.ConfigSnapshot.ModelPath;
        if (!string.IsNullOrWhiteSpace(path))
            return Path.GetFileName(path);
        if (!string.IsNullOrWhiteSpace(run.ConfigSnapshot.HfFile))
            return run.ConfigSnapshot.HfFile;
        if (!string.IsNullOrWhiteSpace(run.ConfigSnapshot.Alias))
            return run.ConfigSnapshot.Alias;
        return "—";
    }

    private static string CacheTypes(BenchmarkRun run)
    {
        var k = string.IsNullOrWhiteSpace(run.ConfigSnapshot.CacheTypeK) ? "—" : run.ConfigSnapshot.CacheTypeK;
        var v = string.IsNullOrWhiteSpace(run.ConfigSnapshot.CacheTypeV) ? "—" : run.ConfigSnapshot.CacheTypeV;
        return $"{k} / {v}";
    }

    private static string Prom(BenchmarkRun run, string key, int decimals = 2) =>
        run.Metrics.Prometheus != null && run.Metrics.Prometheus.TryGetValue(key, out var v)
            ? Num(v, decimals)
            : "—";

    private static string FileNameOr(string? path) =>
        string.IsNullOrWhiteSpace(path) ? "—" : Path.GetFileName(path);

    private static string Flag(bool? value) => value switch
    {
        true => "on",
        false => "off",
        null => "—",
    };

    private static string Str(int? value) =>
        value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "—";

    private static string Num(double? value, int decimals = 2)
    {
        if (!value.HasValue)
            return "—";
        return value.Value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    private static string Cell(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        return text.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    }
}
