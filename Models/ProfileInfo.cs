using System;
using System.Text.Json.Serialization;

namespace LlamaServerLauncher.Models;

public class ProfileInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;

    [JsonPropertyName("lastModified")]
    public DateTime LastModified { get; set; }

    [JsonPropertyName("configuration")]
    public ServerConfiguration Configuration { get; set; } = new();
}