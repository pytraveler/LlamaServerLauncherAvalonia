using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LlamaServerLauncher.Models.Benchmarking;

namespace LlamaServerLauncher.Services.Benchmarking;

public sealed class BenchmarkMetricRow
{
    public string Key { get; }
    public string Label { get; }
    public string Group { get; }

    public BenchmarkMetricRow(string key, string label, string group)
    {
        Key = key;
        Label = label;
        Group = group;
    }
}

public static class BenchmarkReportBuilder
{
    public const string GroupResults = "Results";
    public const string GroupServer = "Server metrics";
    public const string GroupConfig = "Configuration";
    public const string GroupEnvironment = "Environment";

    private static readonly (string Key, string Label, string Group, Func<BenchmarkRun, string> Value)[] Rows =
    {
        ("profile", "Profile", GroupResults, r => r.ProfileName),
        ("date", "Date", GroupResults, r => r.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
        ("model", "Model", GroupResults, r => ModelName(r)),
        ("gen-tps", "Gen tok/s", GroupResults, r => Num(r.Metrics.StdGenTps ?? r.Metrics.PromptRunGenTps ?? r.Metrics.LogGenTps ?? r.Metrics.PredictedTokensSeconds)),
        ("prompt-tps", "Prompt tok/s", GroupResults, r => Num(r.Metrics.StdPromptTps ?? r.Metrics.PromptRunPromptTps ?? r.Metrics.LogPromptTps ?? r.Metrics.PromptTokensSeconds)),
        ("ttft", "TTFT, ms", GroupResults, r => Num(r.Metrics.StdTtftMs ?? r.Metrics.PromptRunTtftMs)),
        ("prompt-run-requests", "Prompt run requests", GroupResults, r => r.Metrics.PromptRunTurns is int n ? n.ToString(CultureInfo.InvariantCulture) : "-"),
        ("predicted-tokens-seconds", "predicted_tokens_seconds", GroupServer, r => Num(r.Metrics.PredictedTokensSeconds)),
        ("prompt-tokens-seconds", "prompt_tokens_seconds", GroupServer, r => Num(r.Metrics.PromptTokensSeconds)),
        ("kv-cache-usage-ratio", "kv_cache_usage_ratio", GroupServer, r => Num(r.Metrics.KvCacheUsageRatio, 4)),
        ("kv-cache-tokens", "kv_cache_tokens", GroupServer, r => Prom(r, "llamacpp:kv_cache_tokens", 0)),
        ("tokens-predicted-total", "tokens_predicted_total", GroupServer, r => Num(r.Metrics.TokensPredictedTotal, 0)),
        ("prompt-tokens-total", "prompt_tokens_total", GroupServer, r => Num(r.Metrics.PromptTokensTotal, 0)),
        ("n-decode-total", "n_decode_total", GroupServer, r => Prom(r, "llamacpp:n_decode_total", 0)),
        ("n-busy-slots-per-decode", "n_busy_slots_per_decode", GroupServer, r => Prom(r, "llamacpp:n_busy_slots_per_decode")),
        ("requests-processing", "requests_processing", GroupServer, r => Prom(r, "llamacpp:requests_processing", 0)),
        ("requests-deferred", "requests_deferred", GroupServer, r => Prom(r, "llamacpp:requests_deferred", 0)),
        ("duration", "Duration, s", GroupResults, r => Num(r.Metrics.DurationSeconds, 1)),
        ("ngl", "ngl", GroupConfig, r => Str(r.ConfigSnapshot.GpuLayers)),
        ("ctx", "ctx", GroupConfig, r => Str(r.ConfigSnapshot.ContextSize)),
        ("batch", "batch", GroupConfig, r => Str(r.ConfigSnapshot.BatchSize)),
        ("ubatch", "ubatch", GroupConfig, r => Str(r.ConfigSnapshot.UBatchSize)),
        ("threads", "threads", GroupConfig, r => Str(r.ConfigSnapshot.Threads)),
        ("flash-attn", "flash-attn", GroupConfig, r => Flag(r.ConfigSnapshot.FlashAttention)),
        ("n-cpu-moe", "n-cpu-moe", GroupConfig, r => Str(r.ConfigSnapshot.CpuMoe)),
        ("cache-kv", "cache K/V", GroupConfig, r => CacheTypes(r)),
        ("seed", "seed", GroupConfig, r => Str(r.ConfigSnapshot.Seed)),
        ("temp", "temp", GroupConfig, r => Num(r.ConfigSnapshot.Temperature)),
        ("parallel", "parallel", GroupConfig, r => Str(r.ConfigSnapshot.ParallelSlots)),
        ("mmap", "mmap", GroupConfig, r => Flag(r.ConfigSnapshot.Mmap)),
        ("mlock", "mlock", GroupConfig, r => Flag(r.ConfigSnapshot.Mlock)),
        ("mmproj", "mmproj", GroupConfig, r => FileNameOr(r.ConfigSnapshot.MmprojPath)),
        ("draft-model", "Draft model", GroupConfig, r => FileNameOr(r.ConfigSnapshot.SpecDraftModel)),
        ("custom-args", "Custom args", GroupConfig, r => string.IsNullOrWhiteSpace(r.ConfigSnapshot.CustomArguments) ? "—" : r.ConfigSnapshot.CustomArguments.Trim()),
        ("docker", "docker", GroupEnvironment, r => r.RunInDocker ? "yes" : "no"),
        ("llama-cpp", "llama.cpp", GroupEnvironment, r => string.IsNullOrWhiteSpace(r.LlamaVersion) ? "—" : r.LlamaVersion!),
        ("hardware", "Hardware", GroupEnvironment, r => string.IsNullOrWhiteSpace(r.HardwareSummary) ? "—" : r.HardwareSummary!),
    };

    public static IReadOnlyList<BenchmarkMetricRow> AvailableRows { get; } =
        Rows.Select(r => new BenchmarkMetricRow(r.Key, r.Label, r.Group)).ToArray();

    public static IReadOnlyList<string> AllRowKeys { get; } = Rows.Select(r => r.Key).ToArray();

    public static string BuildComparison(IReadOnlyList<BenchmarkRun> runs, Func<string, string>? localize = null,
        IReadOnlyCollection<string>? rowKeys = null)
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

        foreach (var (_, label, _, value) in SelectRows(rowKeys))
        {
            sb.Append("| ").Append(Cell(L(label))).Append(" |");
            foreach (var run in runs)
                sb.Append(' ').Append(Cell(value(run))).Append(" |");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static IEnumerable<(string Key, string Label, string Group, Func<BenchmarkRun, string> Value)> SelectRows(
        IReadOnlyCollection<string>? rowKeys)
    {
        if (rowKeys == null)
            return Rows;
        var wanted = new HashSet<string>(rowKeys, StringComparer.OrdinalIgnoreCase);
        return Rows.Where(r => wanted.Contains(r.Key));
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
        foreach (var (_, label, _, value) in Rows)
            sb.Append("| ").Append(Cell(L(label))).Append(" | ").Append(Cell(value(run))).AppendLine(" |");

        if (run.PromptRun != null && run.PromptRun.Turns.Count > 0)
        {
            sb.AppendLine();
            sb.Append(PromptRunDocument.BuildSummarySection(run.PromptRun, localize));
        }

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
