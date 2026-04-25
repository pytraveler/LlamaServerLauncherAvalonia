using System.Collections.Generic;

namespace LlamaServerLauncher.Models;

public class AppSettings
{
    public double WindowWidth { get; set; } = 900;
    public double WindowHeight { get; set; } = 650;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    public string Language { get; set; } = "en";

    public string ProfileNameInput { get; set; } = "";
    
    public string ExecutablePath { get; set; } = "";
    public string ModelPath { get; set; } = "";
    public string ModelsDir { get; set; } = "";
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 8080;
    public string ContextSize { get; set; } = "";
    public string Threads { get; set; } = "";
    public string GpuLayers { get; set; } = "";
    public string Temperature { get; set; } = "";
    public string MaxTokens { get; set; } = "";
    public string BatchSize { get; set; } = "";
    public string UBatchSize { get; set; } = "";
    public string MinP { get; set; } = "";
    public string MmprojPath { get; set; } = "";
    public string CacheTypeK { get; set; } = "";
    public string CacheTypeV { get; set; } = "";
    public string TopK { get; set; } = "";
    public string TopP { get; set; } = "";
    public string RepeatPenalty { get; set; } = "";
    public bool? FlashAttention { get; set; }
    public bool? EnableWebUI { get; set; }
    public bool? EmbeddingMode { get; set; }
    public bool? EnableSlots { get; set; }
    public bool? EnableMetrics { get; set; }
    public string ApiKey { get; set; } = "";
    public string LogFilePath { get; set; } = "";
    public bool VerboseLogging { get; set; }
    public string Alias { get; set; } = "";
    public string CustomArguments { get; set; } = "";
    public bool AutoRestart { get; set; }
    public bool AutoScrollLog { get; set; } = true;
    public bool LogEnabled { get; set; } = true;
    public bool LogVisible { get; set; } = true;
    public Dictionary<string, bool> CustomArgumentToggleStates { get; set; } = new();
    public string FontSizeLevel { get; set; } = "Medium";
    public bool AutoFitHeight { get; set; }
    public double AutoFitHeightSavedHeight { get; set; } = 650;
    public bool TabPanelVisible { get; set; } = true;
}