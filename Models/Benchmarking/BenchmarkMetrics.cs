using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LlamaServerLauncher.Models.Benchmarking;

public class BenchmarkMetrics
{
    [JsonPropertyName("durationSeconds")]
    public double DurationSeconds { get; set; }

    [JsonPropertyName("logGenTps")]
    public double? LogGenTps { get; set; }

    [JsonPropertyName("logPromptTps")]
    public double? LogPromptTps { get; set; }

    [JsonPropertyName("prometheus")]
    public Dictionary<string, double> Prometheus { get; set; } = new();

    [JsonPropertyName("predictedTokensSeconds")]
    public double? PredictedTokensSeconds { get; set; }

    [JsonPropertyName("promptTokensSeconds")]
    public double? PromptTokensSeconds { get; set; }

    [JsonPropertyName("kvCacheUsageRatio")]
    public double? KvCacheUsageRatio { get; set; }

    [JsonPropertyName("tokensPredictedTotal")]
    public double? TokensPredictedTotal { get; set; }

    [JsonPropertyName("promptTokensTotal")]
    public double? PromptTokensTotal { get; set; }

    [JsonPropertyName("stdGenTps")]
    public double? StdGenTps { get; set; }

    [JsonPropertyName("stdPromptTps")]
    public double? StdPromptTps { get; set; }

    [JsonPropertyName("stdTtftMs")]
    public double? StdTtftMs { get; set; }

    [JsonPropertyName("stdRepeat")]
    public int? StdRepeat { get; set; }

    [JsonPropertyName("stdPromptTokens")]
    public int? StdPromptTokens { get; set; }

    [JsonPropertyName("stdNPredict")]
    public int? StdNPredict { get; set; }

    [JsonPropertyName("promptRunTurns")]
    public int? PromptRunTurns { get; set; }

    [JsonPropertyName("promptRunGenTps")]
    public double? PromptRunGenTps { get; set; }

    [JsonPropertyName("promptRunPromptTps")]
    public double? PromptRunPromptTps { get; set; }

    [JsonPropertyName("promptRunTtftMs")]
    public double? PromptRunTtftMs { get; set; }
}
