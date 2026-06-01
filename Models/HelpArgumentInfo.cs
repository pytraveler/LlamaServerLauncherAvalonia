using System.Collections.Generic;

namespace LlamaServerLauncher.Models;

public class HelpArgumentInfo
{
    public string PrimaryFlag { get; set; } = "";
    public List<string> AllFlags { get; set; } = new();
    public string Description { get; set; } = "";
    public string? DefaultValue { get; set; }
    public string Category { get; set; } = "other";
    public List<string>? AllowedValues { get; set; }
}
