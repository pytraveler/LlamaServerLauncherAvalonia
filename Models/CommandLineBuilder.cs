using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LlamaServerLauncher.Models;

public static class CommandLineBuilder
{
    public static string Build(ServerConfiguration config)
    {
        var args = new List<string>();
        
        var processedProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var normalizedCustomArgs = CommandLineParser.NormalizeSpecialCharacters(config.CustomArguments);
        Dictionary<string, string?> customArgValues = new(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(normalizedCustomArgs))
        {
            var parsed = CommandLineParser.ParseArguments(normalizedCustomArgs);
            customArgValues = CommandLineParser.GetArgumentValues(parsed);
        }

        bool IsOverriddenByAlias(string flag)
        {
            if (customArgValues.ContainsKey(flag))
                return true;
            
            foreach (var kvp in ServerConfiguration.KnownArguments)
            {
                if (kvp.Value.PropertyName == GetPropertyNameForFlag(flag) && 
                    customArgValues.ContainsKey(kvp.Key))
                {
                    return true;
                }
            }
            
            return false;
        }

        string? GetCustomValue(string flag)
        {
            if (customArgValues.TryGetValue(flag, out var val) && !string.IsNullOrWhiteSpace(val))
                return val;
            
            foreach (var kvp in ServerConfiguration.KnownArguments)
            {
                if (kvp.Value.PropertyName == GetPropertyNameForFlag(flag))
                {
                    if (customArgValues.TryGetValue(kvp.Key, out val) && !string.IsNullOrWhiteSpace(val))
                        return val;
                }
            }
            
            return null;
        }

        bool IsFlagPresentInCustomArgs(string flag)
        {
            if (customArgValues.ContainsKey(flag))
                return true;
            
            foreach (var kvp in ServerConfiguration.KnownArguments)
            {
                if (kvp.Value.PropertyName == GetPropertyNameForFlag(flag))
                {
                    if (customArgValues.ContainsKey(kvp.Key))
                        return true;
                }
            }
            
            return false;
        }

        string? GetActualCustomFlag(string flag, string invertedFlag = "")
        {
            string? propertyName = GetPropertyNameForFlag(flag);
            if (propertyName == null)
            {
                return customArgValues.ContainsKey(flag) ? flag : null;
            }
            
            foreach (var kvp in ServerConfiguration.KnownArguments)
            {
                if (kvp.Value.PropertyName.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    if (customArgValues.ContainsKey(kvp.Key))
                        return kvp.Key;
                }
            }
            
            return null;
        }

        void AddIfNotOverridden(List<string> list, string flag, string? uiValue)
        {
            string? customValue = GetCustomValue(flag);
            
            if (customValue != null)
            {
                list.Add($"{flag} {QuoteIfNeeded(customValue)}");
            }
            else if (!IsFlagPresentInCustomArgs(flag) && !string.IsNullOrEmpty(uiValue))
            {
                list.Add($"{flag} {uiValue}");
            }
        }

        void AddBoolOnOff(List<string> list, string flag, bool? uiValue, string invertedFlag = "")
        {
            string? customValue = GetCustomValue(flag);
            
            if (customValue != null)
            {
                list.Add($"{flag} {customValue}");
            }
            else if (uiValue.HasValue)
            {
                list.Add($"{flag} {(uiValue.Value ? "on" : "off")}");
            }
        }

        void AddBoolFlag(List<string> list, string flag, bool? uiValue, string invertedFlag = "")
        {
            string? propertyName = GetPropertyNameForFlag(flag);
            if (propertyName != null && !processedProperties.Add(propertyName))
                return;
            
            string? actualFlag = GetActualCustomFlag(flag, invertedFlag);
            
            if (actualFlag != null)
            {
                list.Add(actualFlag);
                return;
            }

            if (!uiValue.HasValue)
                return;

            if (uiValue.Value)
            {
                list.Add(flag);
            }
            else if (!string.IsNullOrEmpty(invertedFlag))
            {
                list.Add(invertedFlag);
            }
        }

        if (!IsOverriddenByAlias("-m"))
        {
            if (!string.IsNullOrEmpty(config.ModelPath))
                args.Add($"-m \"{EscapePath(config.ModelPath)}\"");
            else if (!string.IsNullOrEmpty(config.ModelsDir))
                args.Add($"--models-dir \"{EscapePath(config.ModelsDir)}\"");
        }

        AddIfNotOverridden(args, "--host", config.Host);
        AddIfNotOverridden(args, "--port", config.Port.ToString());
        AddIfNotOverridden(args, "-c", config.ContextSize?.ToString());
        AddIfNotOverridden(args, "-t", config.Threads?.ToString());
        AddIfNotOverridden(args, "-ngl", config.GpuLayers?.ToString());
        AddIfNotOverridden(args, "--temp", config.Temperature?.ToString(CultureInfo.InvariantCulture));
        AddIfNotOverridden(args, "-n", config.MaxTokens?.ToString());
        AddIfNotOverridden(args, "-b", config.BatchSize?.ToString());
        AddIfNotOverridden(args, "-ub", config.UBatchSize?.ToString());
        AddIfNotOverridden(args, "--min-p", config.MinP?.ToString(CultureInfo.InvariantCulture));
        
        string? mmCustomValue = GetCustomValue("-mm");
        
        if (mmCustomValue != null)
        {
            args.Add($"-mm {QuoteIfNeeded(mmCustomValue)}");
        }
        else if (!string.IsNullOrEmpty(config.MmprojPath))
        {
            args.Add($"-mm \"{EscapePath(config.MmprojPath)}\"");
        }
        
        AddIfNotOverridden(args, "-ctk", config.CacheTypeK);
        AddIfNotOverridden(args, "-ctv", config.CacheTypeV);

        AddIfNotOverridden(args, "--top-k", config.TopK?.ToString(CultureInfo.InvariantCulture));
        AddIfNotOverridden(args, "--top-p", config.TopP?.ToString(CultureInfo.InvariantCulture));
        AddIfNotOverridden(args, "--repeat-penalty", config.RepeatPenalty?.ToString(CultureInfo.InvariantCulture));

        AddBoolOnOff(args, "-fa", config.FlashAttention);

        string? actualWebuiFlag = GetActualCustomFlag("--webui", "--no-webui");
        
        if (actualWebuiFlag != null)
        {
            args.Add(actualWebuiFlag);
        }
        else if (config.EnableWebUI == true)
        {
            args.Add("--webui");
        }
        else if (config.EnableWebUI == false)
        {
            args.Add("--no-webui");
        }

        AddBoolFlag(args, "--embedding", config.EmbeddingMode);
        AddBoolFlag(args, "--embeddings", config.EmbeddingMode);
        AddBoolFlag(args, "--slots", config.EnableSlots, "--no-slots");
        AddBoolFlag(args, "--metrics", config.EnableMetrics);

        AddIfNotOverridden(args, "--api-key", string.IsNullOrEmpty(config.ApiKey) ? null : $"\"{config.ApiKey}\"");
        AddIfNotOverridden(args, "--log-file", string.IsNullOrEmpty(config.LogFilePath) ? null : $"\"{config.LogFilePath}\"");
        AddIfNotOverridden(args, "--alias", string.IsNullOrEmpty(config.Alias) ? null : $"\"{config.Alias}\"");

        string? actualVerboseFlag = GetActualCustomFlag("-v", "--verbose");
        
        if (actualVerboseFlag != null)
        {
            args.Add(actualVerboseFlag);
        }
        else if (config.VerboseLogging)
        {
            args.Add("-v");
        }

        AddRemainingCustomArgs(args, normalizedCustomArgs, customArgValues);

        return string.Join(" ", args);
    }

    private static string? GetPropertyNameForFlag(string flag)
    {
        if (ServerConfiguration.KnownArguments.TryGetValue(flag, out var mapping))
            return mapping.PropertyName;
        return null;
    }

    private static string QuoteIfNeeded(string value)
    {
        if (value.Contains(' ') || value.Contains('\t') || value.Contains('"') || value.Contains('\''))
        {
            return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
        }
        return value;
    }

    private static string? StripQuotes(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        
        if ((value.StartsWith('"') && value.EndsWith('"')) || 
            (value.StartsWith('\'') && value.EndsWith('\'')))
        {
            return value.Length > 2 ? value[1..^1] : string.Empty;
        }
        
        return value;
    }

    private static string EscapePath(string path)
    {
        return path.Replace("\\", "\\\\");
    }

    public static string UnescapePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        
        return path.Replace(@"\\", @"\");
    }

    private static readonly HashSet<string> PathProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "ModelPath",
        "ModelsDir",
        "MmprojPath",
        "ExecutablePath"
    };

    public static bool IsPathProperty(string propertyName) => PathProperties.Contains(propertyName);

    private static void AddRemainingCustomArgs(List<string> args, string normalizedCustomArgs, Dictionary<string, string?> usedCustomValues)
    {
        if (string.IsNullOrEmpty(normalizedCustomArgs))
            return;

        var parsed = CommandLineParser.ParseArguments(normalizedCustomArgs);
        var usedFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in ServerConfiguration.KnownArguments)
        {
            if (usedCustomValues.ContainsKey(kvp.Key) || 
                (usedCustomValues.ContainsKey(kvp.Key) && usedCustomValues[kvp.Key] != null))
            {
                usedFlags.Add(kvp.Key);
            }
        }

        for (int i = 0; i < parsed.Count; i++)
        {
            string arg = parsed[i];
            if (!arg.StartsWith("-"))
                continue;

            if (usedFlags.Contains(arg))
                continue;

            if (i + 1 < parsed.Count && !parsed[i + 1].StartsWith("-"))
            {
                args.Add($"{arg} {QuoteIfNeeded(parsed[i + 1])}");
                i++;
            }
            else
            {
                args.Add(arg);
            }
        }
    }

    public static string BuildFullCommand(ServerConfiguration config)
    {
        var args = Build(config);
        return $"\"{config.ExecutablePath}\" {args}";
    }
}