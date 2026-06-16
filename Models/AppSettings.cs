using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LlamaServerLauncher.Models;

public class DialogGeometry
{
    [JsonPropertyName("width")]
    public double Width { get; set; }
    [JsonPropertyName("height")]
    public double Height { get; set; }
    [JsonPropertyName("left")]
    public double? Left { get; set; }
    [JsonPropertyName("top")]
    public double? Top { get; set; }
}

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
    public string ParallelSlots { get; set; } = "";
    public bool? ContBatching { get; set; }
    public string Timeout { get; set; } = "";
    public bool? CachePrompt { get; set; }
    public bool? Mlock { get; set; }
    public bool? Mmap { get; set; }
    public bool? Reasoning { get; set; }
    public string ReasoningBudget { get; set; } = "";
    public string Seed { get; set; } = "";
    public string PresencePenalty { get; set; } = "";
    public string FrequencyPenalty { get; set; } = "";
    public bool? ContextShift { get; set; }
    public string SpecType { get; set; } = "";
    public string SpecDraftModel { get; set; } = "";
    public string SpecDraftGpuLayers { get; set; } = "";
    public string SpecDraftNMax { get; set; } = "";
    public string SpecDraftNMin { get; set; } = "";
    public string SpecDraftPSplit { get; set; } = "";
    public string SpecDraftPMin { get; set; } = "";
    public string HfRepo { get; set; } = "";
    public string HfFile { get; set; } = "";
    public bool Offline { get; set; }
    public string HfRepoDraft { get; set; } = "";
    public bool AutoRestart { get; set; }
    public string CustomBrowserPath { get; set; } = "";
    public bool AutoStartWithSystem { get; set; }
    public bool ConfirmStopServer { get; set; } = true;
    public bool AutoScrollLog { get; set; } = true;
    public bool LogEnabled { get; set; } = true;
    public bool LogVisible { get; set; } = true;
    public Dictionary<string, bool> CustomArgumentToggleStates { get; set; } = new();
    public string FontSizeLevel { get; set; } = "Medium";
    public string ThemeVariant { get; set; } = "Dark";
    public string ColorScheme { get; set; } = "Default";
    public Dictionary<string, string> CustomColors { get; set; } = new();
    public string FontFamily { get; set; } = "";
    public bool AutoFitHeight { get; set; }
    public double AutoFitHeightSavedHeight { get; set; } = 650;
    public bool TabPanelVisible { get; set; } = true;
    public bool IsNavPaneOpen { get; set; } = true;
    public double LogHeight { get; set; } = 200;
    public string LlamaCppInstalledTag { get; set; } = "";
    public string LlamaCppCustomDownloadPath { get; set; } = "";
    public int SelectedTabIndex { get; set; }
    public Dictionary<string, List<string>> RecentValuesHistory { get; set; } = new();
    public Dictionary<string, string> ReleaseBodyCache { get; set; } = new();
    public List<string> ReleaseBodyCacheOrder { get; set; } = new();
    public int MaxLogFiles { get; set; } = 5;
    public long MaxLogSizeBytes { get; set; } = 10 * 1024 * 1024;

    public bool RunInDocker { get; set; }
    public string DockerImage { get; set; } = "ghcr.io/ggml-org/llama.cpp:server";
    public bool DockerGpuAll { get; set; }
    public bool DockerRm { get; set; } = true;
    public string DockerContainerName { get; set; } = "";

    public DateTime LastAppUpdateCheck { get; set; }
    public DateTime LastLlamaUpdateCheck { get; set; }
    public int AppUpdateCheckIntervalMinutes { get; set; } = 15;
    public int LlamaUpdateCheckIntervalMinutes { get; set; } = 15;
    public string CachedLlamaReleasesJson { get; set; } = "";
    public DateTime CachedLlamaReleasesTimestamp { get; set; }

    public bool LogStreamEnabled { get; set; }
    public int LogStreamPort { get; set; } = 5872;
    public string LogStreamToken { get; set; } = "";

    public bool ScenariosEnabled { get; set; }
    public string SelectedScenario { get; set; } = "";

    public Dictionary<string, DialogGeometry> DialogGeometry { get; set; } = new();

    public bool ExperimentalReposEnabled { get; set; }
    public int ExperimentalUpdateCheckIntervalMinutes { get; set; } = 480;
    public List<ExperimentalRepoInfo> ExperimentalRepos { get; set; } = new();
    public DateTime LastExperimentalUpdateCheck { get; set; }
}