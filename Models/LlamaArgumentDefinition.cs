using System.Collections.Generic;

namespace LlamaServerLauncher.Models;

public class LlamaArgumentDefinition
{
    public string PrimaryFlag { get; set; } = "";
    public List<string> Aliases { get; set; } = new();
    public string DescriptionEn { get; set; } = "";
    public string DescriptionRu { get; set; } = "";
    public string? DefaultValue { get; set; }
    public List<string>? AllowedValues { get; set; }
    public string Category { get; set; } = "common";
}
