using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using LlamaServerLauncher.Services;

namespace LlamaServerLauncher.Models;

public class ExperimentalRepoInfo
{
    [JsonPropertyName("repoUrl")]
    public string RepoUrl { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("filterTags")]
    public string FilterTags { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("cachedReleasesTimestamp")]
    public DateTime CachedReleasesTimestamp { get; set; }

    // Serialized directly into AppSettings (single-encoded). Release bodies are not
    // cached (the experimental UI never shows them), keeping the file small.
    [JsonPropertyName("cachedReleases")]
    public List<ReleaseInfo> CachedReleases { get; set; } = new();

    public override string ToString() => !string.IsNullOrEmpty(DisplayName) ? DisplayName : RepoUrl;
}
