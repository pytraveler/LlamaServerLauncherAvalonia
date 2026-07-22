using System.Collections.Generic;
using System.Globalization;
using LlamaServerLauncher.Models.Benchmarking;

namespace LlamaServerLauncher.Services.Benchmarking;

public static class PrometheusMetricsParser
{
    public static Dictionary<string, double> Parse(string? text)
    {
        var result = new Dictionary<string, double>();
        if (string.IsNullOrEmpty(text))
            return result;

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;

            string name;
            string rest;

            int brace = line.IndexOf('{');
            if (brace >= 0)
            {
                int close = line.IndexOf('}', brace + 1);
                if (close < 0)
                    continue;
                name = line.Substring(0, brace).Trim();
                rest = line.Substring(close + 1).Trim();
            }
            else
            {
                int sp = line.IndexOf(' ');
                if (sp < 0)
                    continue;
                name = line.Substring(0, sp).Trim();
                rest = line.Substring(sp + 1).Trim();
            }

            if (name.Length == 0 || rest.Length == 0)
                continue;

            int valueEnd = rest.IndexOf(' ');
            string valueToken = valueEnd < 0 ? rest : rest.Substring(0, valueEnd);

            if (double.TryParse(valueToken, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                && !double.IsNaN(value) && !double.IsInfinity(value))
            {
                result[name] = value;
            }
        }

        return result;
    }

    public static void ApplyKnown(Dictionary<string, double> metrics, BenchmarkMetrics target)
    {
        target.PredictedTokensSeconds = Get(metrics, "llamacpp:predicted_tokens_seconds");
        target.PromptTokensSeconds = Get(metrics, "llamacpp:prompt_tokens_seconds");
        target.KvCacheUsageRatio = Get(metrics, "llamacpp:kv_cache_usage_ratio");
        target.TokensPredictedTotal = Get(metrics, "llamacpp:tokens_predicted_total");
        target.PromptTokensTotal = Get(metrics, "llamacpp:prompt_tokens_total");
    }

    private static double? Get(Dictionary<string, double> metrics, string key) =>
        metrics.TryGetValue(key, out var value) ? value : null;
}
