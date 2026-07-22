using System;
using System.Text.Json.Serialization;

namespace LlamaServerLauncher.Models.Benchmarking;

public class BenchmarkRun
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("profileName")]
    public string ProfileName { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;

    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("runInDocker")]
    public bool RunInDocker { get; set; }

    [JsonPropertyName("configSnapshot")]
    public ServerConfiguration ConfigSnapshot { get; set; } = new();

    [JsonPropertyName("metrics")]
    public BenchmarkMetrics Metrics { get; set; } = new();

    [JsonPropertyName("hardwareSummary")]
    public string? HardwareSummary { get; set; }

    [JsonPropertyName("llamaVersion")]
    public string? LlamaVersion { get; set; }

    [JsonIgnore]
    public string DirectoryPath { get; set; } = string.Empty;

    [JsonIgnore]
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Label) ? Id : $"{Label} ({Id})";
}
