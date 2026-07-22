using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LlamaServerLauncher.Models.Benchmarking;

public class BenchmarkRunRef
{
    [JsonPropertyName("profileName")]
    public string ProfileName { get; set; } = string.Empty;

    [JsonPropertyName("runId")]
    public string RunId { get; set; } = string.Empty;
}

public class BenchmarkComparisonSet
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("runs")]
    public List<BenchmarkRunRef> Runs { get; set; } = new();
}
