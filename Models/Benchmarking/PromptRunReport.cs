using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace LlamaServerLauncher.Models.Benchmarking;

public class PromptRunTurn
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    [JsonPropertyName("response")]
    public string Response { get; set; } = string.Empty;

    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; set; }

    [JsonPropertyName("finishReason")]
    public string? FinishReason { get; set; }

    [JsonPropertyName("promptTokens")]
    public int? PromptTokens { get; set; }

    [JsonPropertyName("predictedTokens")]
    public int? PredictedTokens { get; set; }

    [JsonPropertyName("promptTps")]
    public double? PromptTps { get; set; }

    [JsonPropertyName("genTps")]
    public double? GenTps { get; set; }

    [JsonPropertyName("ttftMs")]
    public double? TtftMs { get; set; }

    [JsonPropertyName("durationSeconds")]
    public double DurationSeconds { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonIgnore]
    public bool Failed => !string.IsNullOrEmpty(Error);
}

public class PromptRunReport
{
    [JsonPropertyName("systemPrompt")]
    public string SystemPrompt { get; set; } = string.Empty;

    [JsonPropertyName("keepContext")]
    public bool KeepContext { get; set; } = true;

    [JsonPropertyName("maxTokens")]
    public int MaxTokens { get; set; }

    [JsonPropertyName("turns")]
    public List<PromptRunTurn> Turns { get; set; } = new();

    [JsonIgnore]
    public int CompletedTurns => Turns.Count(t => !t.Failed);

    [JsonIgnore]
    public int FailedTurns => Turns.Count(t => t.Failed);

    [JsonIgnore]
    public double TotalDurationSeconds => Turns.Sum(t => t.DurationSeconds);

    [JsonIgnore]
    public double? AvgGenTps => Avg(Turns.Where(t => !t.Failed).Select(t => t.GenTps));

    [JsonIgnore]
    public double? AvgPromptTps => Avg(Turns.Where(t => !t.Failed).Select(t => t.PromptTps));

    [JsonIgnore]
    public double? AvgTtftMs => Avg(Turns.Where(t => !t.Failed).Select(t => t.TtftMs));

    private static double? Avg(IEnumerable<double?> values)
    {
        var list = values.Where(v => v.HasValue && v.Value > 0).Select(v => v!.Value).ToList();
        return list.Count > 0 ? list.Average() : (double?)null;
    }
}
