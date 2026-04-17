using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LlamaServerLauncher.Models;

public class ServerConfiguration
{
    [JsonPropertyName("executablePath")]
    public string ExecutablePath { get; set; } = string.Empty;

    [JsonPropertyName("modelPath")]
    public string ModelPath { get; set; } = string.Empty;

    [JsonPropertyName("modelsDir")]
    public string ModelsDir { get; set; } = string.Empty;

    [JsonPropertyName("host")]
    public string Host { get; set; } = "127.0.0.1";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 8080;

    [JsonPropertyName("contextSize")]
    public int? ContextSize { get; set; }

    [JsonPropertyName("threads")]
    public int? Threads { get; set; }

    [JsonPropertyName("gpuLayers")]
    public int? GpuLayers { get; set; }

    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    [JsonPropertyName("maxTokens")]
    public int? MaxTokens { get; set; }

    [JsonPropertyName("batchSize")]
    public int? BatchSize { get; set; }

    [JsonPropertyName("uBatchSize")]
    public int? UBatchSize { get; set; }

    [JsonPropertyName("minP")]
    public double? MinP { get; set; }

    [JsonPropertyName("mmprojPath")]
    public string MmprojPath { get; set; } = string.Empty;

    [JsonPropertyName("cacheTypeK")]
    public string CacheTypeK { get; set; } = string.Empty;

    [JsonPropertyName("cacheTypeV")]
    public string CacheTypeV { get; set; } = string.Empty;

    [JsonPropertyName("topK")]
    public int? TopK { get; set; }

    [JsonPropertyName("topP")]
    public double? TopP { get; set; }

    [JsonPropertyName("repeatPenalty")]
    public double? RepeatPenalty { get; set; }

    [JsonPropertyName("flashAttention")]
    public bool? FlashAttention { get; set; }

    [JsonPropertyName("enableWebUI")]
    public bool? EnableWebUI { get; set; }

    [JsonPropertyName("embeddingMode")]
    public bool? EmbeddingMode { get; set; }

    [JsonPropertyName("enableSlots")]
    public bool? EnableSlots { get; set; }

    [JsonPropertyName("enableMetrics")]
    public bool? EnableMetrics { get; set; }

    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = string.Empty;

    [JsonPropertyName("logFilePath")]
    public string LogFilePath { get; set; } = string.Empty;

    [JsonPropertyName("verboseLogging")]
    public bool VerboseLogging { get; set; }

    [JsonPropertyName("alias")]
    public string Alias { get; set; } = string.Empty;

    [JsonPropertyName("customArguments")]
    public string CustomArguments { get; set; } = string.Empty;

    public ServerConfiguration Clone()
    {
        return new ServerConfiguration
        {
            ExecutablePath = ExecutablePath,
            ModelPath = ModelPath,
            ModelsDir = ModelsDir,
            Host = Host,
            Port = Port,
            ContextSize = ContextSize,
            Threads = Threads,
            GpuLayers = GpuLayers,
            Temperature = Temperature,
            MaxTokens = MaxTokens,
            BatchSize = BatchSize,
            UBatchSize = UBatchSize,
            MinP = MinP,
            MmprojPath = MmprojPath,
            CacheTypeK = CacheTypeK,
            CacheTypeV = CacheTypeV,
            TopK = TopK,
            TopP = TopP,
            RepeatPenalty = RepeatPenalty,
            FlashAttention = FlashAttention,
            EnableWebUI = EnableWebUI,
            EmbeddingMode = EmbeddingMode,
            EnableSlots = EnableSlots,
            EnableMetrics = EnableMetrics,
            ApiKey = ApiKey,
            LogFilePath = LogFilePath,
            VerboseLogging = VerboseLogging,
            Alias = Alias,
            CustomArguments = CustomArguments
        };
    }

    public static readonly Dictionary<string, ArgumentMapping> KnownArguments = new(StringComparer.OrdinalIgnoreCase)
    {
        ["-m"] = new("ModelPath", ArgType.String),
        ["--model"] = new("ModelPath", ArgType.String),
        ["--models-dir"] = new("ModelsDir", ArgType.String),
        ["--host"] = new("Host", ArgType.String),
        ["--port"] = new("Port", ArgType.Int),
        ["-c"] = new("ContextSize", ArgType.Int),
        ["--ctx-size"] = new("ContextSize", ArgType.Int),
        ["-t"] = new("Threads", ArgType.Int),
        ["--threads"] = new("Threads", ArgType.Int),
        ["-ngl"] = new("GpuLayers", ArgType.Int),
        ["--gpu-layers"] = new("GpuLayers", ArgType.Int),
        ["--n-gpu-layers"] = new("GpuLayers", ArgType.Int),
        ["--temp"] = new("Temperature", ArgType.Double),
        ["--temperature"] = new("Temperature", ArgType.Double),
        ["-n"] = new("MaxTokens", ArgType.Int),
        ["--predict"] = new("MaxTokens", ArgType.Int),
        ["--n-predict"] = new("MaxTokens", ArgType.Int),
        ["-b"] = new("BatchSize", ArgType.Int),
        ["--batch-size"] = new("BatchSize", ArgType.Int),
        ["-ub"] = new("UBatchSize", ArgType.Int),
        ["--ubatch-size"] = new("UBatchSize", ArgType.Int),
        ["--min-p"] = new("MinP", ArgType.Double),
        ["-mm"] = new("MmprojPath", ArgType.String),
        ["--mmproj"] = new("MmprojPath", ArgType.String),
        ["-ctk"] = new("CacheTypeK", ArgType.String),
        ["--cache-type-k"] = new("CacheTypeK", ArgType.String),
        ["-ctv"] = new("CacheTypeV", ArgType.String),
        ["--cache-type-v"] = new("CacheTypeV", ArgType.String),
        ["--top-k"] = new("TopK", ArgType.Int),
        ["--top-p"] = new("TopP", ArgType.Double),
        ["--repeat-penalty"] = new("RepeatPenalty", ArgType.Double),
        ["-fa"] = new("FlashAttention", ArgType.BoolOnOff),
        ["--flash-attn"] = new("FlashAttention", ArgType.BoolOnOff),
        ["--webui"] = new("EnableWebUI", ArgType.BoolFlag),
        ["--no-webui"] = new("EnableWebUI", ArgType.BoolFlagInverted),
        ["--embedding"] = new("EmbeddingMode", ArgType.BoolFlag),
        ["--embeddings"] = new("EmbeddingMode", ArgType.BoolFlag),
        ["--slots"] = new("EnableSlots", ArgType.BoolFlag),
        ["--no-slots"] = new("EnableSlots", ArgType.BoolFlagInverted),
        ["--metrics"] = new("EnableMetrics", ArgType.BoolFlag),
        ["--api-key"] = new("ApiKey", ArgType.String),
        ["--log-file"] = new("LogFilePath", ArgType.String),
        ["-v"] = new("VerboseLogging", ArgType.BoolSimple),
        ["--verbose"] = new("VerboseLogging", ArgType.BoolSimple),
        ["-a"] = new("Alias", ArgType.String),
        ["--alias"] = new("Alias", ArgType.String),
    };

    public static readonly Dictionary<string, string[]> MutuallyExclusiveGroups = new()
    {
        ["-fa"] = new[] { "-fa", "--flash-attn" },
        ["--flash-attn"] = new[] { "-fa", "--flash-attn" },
        ["--webui"] = new[] { "--webui", "--no-webui" },
        ["--no-webui"] = new[] { "--webui", "--no-webui" },
        ["--embedding"] = new[] { "--embedding", "--embeddings" },
        ["--embeddings"] = new[] { "--embedding", "--embeddings" },
        ["--slots"] = new[] { "--slots", "--no-slots" },
        ["--no-slots"] = new[] { "--slots", "--no-slots" },
    };
}

public class ArgumentMapping
{
    public string PropertyName { get; }
    public ArgType Type { get; }

    public ArgumentMapping(string propertyName, ArgType type)
    {
        PropertyName = propertyName;
        Type = type;
    }
}

public enum ArgType
{
    String,
    Int,
    Double,
    BoolOnOff,
    BoolFlag,
    BoolFlagInverted,
    BoolSimple
}

public static class ServerConfigurationExtensions
{
    public static ServerConfiguration? ParseFromCommandLine(string args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return null;

        var config = new ServerConfiguration();
        var parsedArgs = CommandLineParser.ParseArguments(args);
        var argValues = CommandLineParser.GetArgumentValues(parsedArgs);
        var argFlags = CommandLineParser.GetArgumentFlags(parsedArgs);

        foreach (var kvp in argValues)
        {
            var arg = kvp.Key;
            var value = kvp.Value;

            if (!ServerConfiguration.KnownArguments.TryGetValue(arg, out var mapping))
                continue;

            switch (mapping.Type)
            {
                case ArgType.String:
                    var stringValue = value ?? "";
                    if (CommandLineBuilder.IsPathProperty(mapping.PropertyName))
                    {
                        stringValue = CommandLineBuilder.UnescapePath(stringValue);
                    }
                    SetProperty(config, mapping.PropertyName, stringValue);
                    break;
                case ArgType.Int:
                    if (int.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var intVal))
                        SetProperty(config, mapping.PropertyName, intVal);
                    break;
                case ArgType.Double:
                    if (double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var doubleVal))
                        SetProperty(config, mapping.PropertyName, doubleVal);
                    break;
                case ArgType.BoolOnOff:
                    if (value != null)
                        SetProperty(config, mapping.PropertyName, value.Equals("on", StringComparison.OrdinalIgnoreCase));
                    break;
            }
        }

        foreach (var flag in argFlags)
        {
            if (!ServerConfiguration.KnownArguments.TryGetValue(flag, out var mapping))
                continue;

            switch (mapping.Type)
            {
                case ArgType.BoolFlag:
                    SetProperty(config, mapping.PropertyName, true);
                    break;
                case ArgType.BoolFlagInverted:
                    SetProperty(config, mapping.PropertyName, false);
                    break;
                case ArgType.BoolSimple:
                    SetProperty(config, mapping.PropertyName, true);
                    break;
            }
        }

        var unknownArgs = new List<string>();
        foreach (var arg in parsedArgs)
        {
            if (!arg.StartsWith("-"))
                continue;

            if (ServerConfiguration.KnownArguments.ContainsKey(arg))
                continue;

            unknownArgs.Add(arg);

            if (argValues.TryGetValue(arg, out var val) && val != null)
            {
                unknownArgs.Add(val);
            }
        }

        if (unknownArgs.Count > 0)
        {
            config.CustomArguments = string.Join(" ", unknownArgs);
        }

        return config;
    }

    private static void SetProperty(ServerConfiguration config, string propertyName, object value)
    {
        var prop = typeof(ServerConfiguration).GetProperty(propertyName);
        if (prop != null && prop.CanWrite)
        {
            prop.SetValue(config, value);
        }
    }
}