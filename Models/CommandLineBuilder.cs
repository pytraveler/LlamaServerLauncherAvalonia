using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LlamaServerLauncher.Models;

public static class CommandLineBuilder
{
    public static string Build(ServerConfiguration config, HashSet<string>? supportedFlags = null, List<string>? validSpecTypeValues = null, List<string>? validCacheTypeValues = null)
    {
        var args = new List<string>();
        
        var processedProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var normalizedCustomArgs = CommandLineParser.NormalizeSpecialCharacters(config.CustomArguments);
        Dictionary<string, string?> allCustomArgValues = new(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(normalizedCustomArgs))
        {
            var parsed = CommandLineParser.ParseArguments(normalizedCustomArgs);
            allCustomArgValues = CommandLineParser.GetArgumentValues(parsed);
        }

        var disabledCustomArgs = config.CustomArgumentToggleStates?
            .Where(kvp => !kvp.Value)
            .Select(kvp => kvp.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Dictionary<string, string?> customArgValues = allCustomArgValues
            .Where(kvp => !disabledCustomArgs.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

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

        string? ResolveSupportedFlag(string flag)
        {
            if (supportedFlags == null)
                return flag;

            if (supportedFlags.Contains(flag))
                return flag;

            string? propertyName = GetPropertyNameForFlag(flag);
            if (propertyName == null)
                return null;

            foreach (var kvp in ServerConfiguration.KnownArguments)
            {
                if (kvp.Value.PropertyName.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    if (supportedFlags.Contains(kvp.Key))
                        return kvp.Key;
                }
            }

            return null;
        }

        void AddIfNotOverridden(List<string> list, string flag, string? uiValue)
        {
            string? customValue = GetCustomValue(flag);
            string? resolved = ResolveSupportedFlag(flag);
            
            if (customValue != null)
            {
                if (resolved != null)
                    list.Add($"{resolved} {QuoteIfNeeded(customValue)}");
            }
            else if (!IsFlagPresentInCustomArgs(flag) && !string.IsNullOrEmpty(uiValue))
            {
                if (resolved != null)
                    list.Add($"{resolved} {uiValue}");
            }
        }

        void AddBoolOnOff(List<string> list, string flag, bool? uiValue, string invertedFlag = "")
        {
            string? customValue = GetCustomValue(flag);
            string? resolved = ResolveSupportedFlag(flag);
            
            if (customValue != null)
            {
                if (resolved != null)
                    list.Add($"{resolved} {customValue}");
            }
            else if (uiValue.HasValue)
            {
                if (resolved != null)
                    list.Add($"{resolved} {(uiValue.Value ? "on" : "off")}");
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
                if (ResolveSupportedFlag(flag) != null || ResolveSupportedFlag(invertedFlag) != null)
                    list.Add(actualFlag);
                return;
            }

            if (!uiValue.HasValue)
                return;

            if (uiValue.Value)
            {
                var resolved = ResolveSupportedFlag(flag);
                if (resolved != null)
                    list.Add(resolved);
            }
            else if (!string.IsNullOrEmpty(invertedFlag))
            {
                var resolved = ResolveSupportedFlag(invertedFlag);
                if (resolved != null)
                    list.Add(resolved);
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
        
        bool cacheTypeKValid = string.IsNullOrEmpty(config.CacheTypeK)
            || validCacheTypeValues == null
            || validCacheTypeValues.Count == 0
            || validCacheTypeValues.Contains(config.CacheTypeK);
        if (cacheTypeKValid)
            AddIfNotOverridden(args, "-ctk", config.CacheTypeK);

        bool cacheTypeVValid = string.IsNullOrEmpty(config.CacheTypeV)
            || validCacheTypeValues == null
            || validCacheTypeValues.Count == 0
            || validCacheTypeValues.Contains(config.CacheTypeV);
        if (cacheTypeVValid)
            AddIfNotOverridden(args, "-ctv", config.CacheTypeV);

        AddIfNotOverridden(args, "--top-k", config.TopK?.ToString(CultureInfo.InvariantCulture));
        AddIfNotOverridden(args, "--top-p", config.TopP?.ToString(CultureInfo.InvariantCulture));
        AddIfNotOverridden(args, "--repeat-penalty", config.RepeatPenalty?.ToString(CultureInfo.InvariantCulture));

        AddIfNotOverridden(args, "-np", config.ParallelSlots?.ToString());
        AddIfNotOverridden(args, "-to", config.Timeout?.ToString());
        AddIfNotOverridden(args, "-s", config.Seed?.ToString());
        AddIfNotOverridden(args, "--presence-penalty", config.PresencePenalty?.ToString(CultureInfo.InvariantCulture));
        AddIfNotOverridden(args, "--frequency-penalty", config.FrequencyPenalty?.ToString(CultureInfo.InvariantCulture));
        AddIfNotOverridden(args, "--reasoning-budget", config.ReasoningBudget?.ToString());

        AddBoolOnOff(args, "-rea", config.Reasoning);

        AddBoolFlag(args, "-cb", config.ContBatching, "--no-cont-batching");
        AddBoolFlag(args, "--cache-prompt", config.CachePrompt, "--no-cache-prompt");
        AddBoolFlag(args, "--context-shift", config.ContextShift, "--no-context-shift");
        AddBoolFlag(args, "--mmap", config.Mmap, "--no-mmap");

        if (config.Mlock == true)
        {
            string? mlockProp = GetPropertyNameForFlag("--mlock");
            if (mlockProp != null && !processedProperties.Contains(mlockProp))
            {
                processedProperties.Add(mlockProp);
                args.Add("--mlock");
            }
        }

        AddBoolOnOff(args, "-fa", config.FlashAttention);

        // Spec-type
        if (!string.IsNullOrEmpty(config.SpecType) && config.SpecType != "none")
        {
            bool specTypeValueValid = validSpecTypeValues == null
                || validSpecTypeValues.Count == 0
                || validSpecTypeValues.Contains(config.SpecType);

            if (specTypeValueValid)
            {
                AddIfNotOverridden(args, "--spec-type", config.SpecType);
            }
        }

        // Draft model (-md)
        string? draftModelCustomValue = GetCustomValue("-md");
        var mdResolved = ResolveSupportedFlag("-md");
        if (draftModelCustomValue != null)
        {
            if (mdResolved != null)
                args.Add($"{mdResolved} {QuoteIfNeeded(draftModelCustomValue)}");
        }
        else if (!string.IsNullOrEmpty(config.SpecDraftModel))
        {
            if (mdResolved != null)
                args.Add($"{mdResolved} \"{EscapePath(config.SpecDraftModel)}\"");
        }

        // -ngld
        {
            var ngldVal = string.IsNullOrEmpty(config.SpecDraftGpuLayers) ? null : config.SpecDraftGpuLayers;
            AddIfNotOverridden(args, "-ngld", ngldVal);
        }

        // General spec params
        {
            var nmaxVal = config.SpecDraftNMax?.ToString();
            AddIfNotOverridden(args, "--spec-draft-n-max", nmaxVal);
        }
        {
            var nminVal = config.SpecDraftNMin?.ToString();
            AddIfNotOverridden(args, "--spec-draft-n-min", nminVal);
        }
        {
            var psplitVal = config.SpecDraftPSplit?.ToString(CultureInfo.InvariantCulture);
            AddIfNotOverridden(args, "--spec-draft-p-split", psplitVal);
        }
        {
            var pminVal = config.SpecDraftPMin?.ToString(CultureInfo.InvariantCulture);
            AddIfNotOverridden(args, "--spec-draft-p-min", pminVal);
        }

        // HuggingFace args
        AddIfNotOverridden(args, "-hf", config.HfRepo);
        AddIfNotOverridden(args, "-hff", config.HfFile);
        AddIfNotOverridden(args, "-hfd", config.HfRepoDraft);
        if (config.Offline)
            args.Add("--offline");

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

        AddRemainingCustomArgs(args, normalizedCustomArgs, customArgValues, disabledCustomArgs);

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
        "ExecutablePath",
        "SpecDraftModel"
    };

    public static bool IsPathProperty(string propertyName) => PathProperties.Contains(propertyName);

    private static void AddRemainingCustomArgs(List<string> args, string normalizedCustomArgs, Dictionary<string, string?> usedCustomValues, HashSet<string> disabledCustomArgs)
    {
        if (string.IsNullOrEmpty(normalizedCustomArgs))
            return;

        var parsed = CommandLineParser.ParseArguments(normalizedCustomArgs);

        for (int i = 0; i < parsed.Count; i++)
        {
            string arg = parsed[i];
            if (!arg.StartsWith("-"))
                continue;

            if (disabledCustomArgs.Contains(arg))
            {
                // Skip disabled flag and its value if present
                if (i + 1 < parsed.Count && !parsed[i + 1].StartsWith("-"))
                    i++;
                continue;
            }

            bool alreadyInArgs = false;
            foreach (var existing in args)
            {
                if (existing == arg || existing.StartsWith(arg + " ") || existing.StartsWith(arg + "\t"))
                {
                    alreadyInArgs = true;
                    break;
                }
            }
            if (alreadyInArgs)
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

    public static string BuildFullCommand(ServerConfiguration config, HashSet<string>? supportedFlags = null, List<string>? validSpecTypeValues = null, List<string>? validCacheTypeValues = null)
    {
        if (config.RunInDocker)
            return BuildDockerCommand(config, supportedFlags, validSpecTypeValues, validCacheTypeValues);
        var args = Build(config, supportedFlags, validSpecTypeValues, validCacheTypeValues);
        return $"\"{config.ExecutablePath}\" {args}";
    }

    public static string BuildDockerCommand(ServerConfiguration config, HashSet<string>? supportedFlags = null, List<string>? validSpecTypeValues = null, List<string>? validCacheTypeValues = null)
    {
        var dockerArgs = new List<string>();
        dockerArgs.Add("run");

        if (config.DockerRm)
            dockerArgs.Add("--rm");

        if (config.DockerGpuAll)
            dockerArgs.Add("--gpus all");

        if (!string.IsNullOrWhiteSpace(config.DockerContainerName))
            dockerArgs.Add($"--name \"{config.DockerContainerName}\"");

        dockerArgs.Add("-p");
        dockerArgs.Add($"{config.Port}:{config.Port}");

        var rewrittenConfig = RewritePathsForDocker(config, out var volumes);
        foreach (var vol in volumes)
            dockerArgs.Add($"-v \"{vol}\"");

        dockerArgs.Add(config.DockerImage);

        var llamaArgs = Build(rewrittenConfig, supportedFlags, validSpecTypeValues, validCacheTypeValues);
        if (!string.IsNullOrEmpty(llamaArgs))
            dockerArgs.Add(llamaArgs);

        return "docker " + string.Join(" ", dockerArgs);
    }

    private static ServerConfiguration RewritePathsForDocker(ServerConfiguration config, out List<string> volumes)
    {
        volumes = new List<string>();
        var rewritten = config.Clone();

        rewritten.Host = "0.0.0.0";

        var mountMap = new Dictionary<string, string>();

        if (!string.IsNullOrEmpty(config.ModelPath))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(config.ModelPath));
            if (!string.IsNullOrEmpty(dir) && !mountMap.ContainsKey(dir))
            {
                var mountPoint = $"/models/{mountMap.Count}";
                mountMap[dir] = mountPoint;
            }
        }

        if (!string.IsNullOrEmpty(config.ModelsDir))
        {
            var dir = Path.GetFullPath(config.ModelsDir);
            if (!mountMap.ContainsKey(dir))
            {
                var mountPoint = $"/models/{mountMap.Count}";
                mountMap[dir] = mountPoint;
            }
        }

        if (!string.IsNullOrEmpty(config.MmprojPath))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(config.MmprojPath));
            if (!string.IsNullOrEmpty(dir) && !mountMap.ContainsKey(dir))
            {
                var mountPoint = $"/models/{mountMap.Count}";
                mountMap[dir] = mountPoint;
            }
        }

        if (!string.IsNullOrEmpty(config.SpecDraftModel))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(config.SpecDraftModel));
            if (!string.IsNullOrEmpty(dir) && !mountMap.ContainsKey(dir))
            {
                var mountPoint = $"/models/{mountMap.Count}";
                mountMap[dir] = mountPoint;
            }
        }

        foreach (var kvp in mountMap)
            volumes.Add($"{kvp.Key}:{kvp.Value}");

        if (!string.IsNullOrEmpty(rewritten.ModelPath))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(config.ModelPath));
            if (!string.IsNullOrEmpty(dir) && mountMap.TryGetValue(dir, out var mp))
                rewritten.ModelPath = mp + "/" + Path.GetFileName(config.ModelPath);
        }

        if (!string.IsNullOrEmpty(rewritten.ModelsDir))
        {
            var dir = Path.GetFullPath(config.ModelsDir);
            if (mountMap.TryGetValue(dir, out var mp))
                rewritten.ModelsDir = mp;
        }

        if (!string.IsNullOrEmpty(rewritten.MmprojPath))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(config.MmprojPath));
            if (!string.IsNullOrEmpty(dir) && mountMap.TryGetValue(dir, out var mp))
                rewritten.MmprojPath = mp + "/" + Path.GetFileName(config.MmprojPath);
        }

        if (!string.IsNullOrEmpty(rewritten.SpecDraftModel))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(config.SpecDraftModel));
            if (!string.IsNullOrEmpty(dir) && mountMap.TryGetValue(dir, out var mp))
                rewritten.SpecDraftModel = mp + "/" + Path.GetFileName(config.SpecDraftModel);
        }

        return rewritten;
    }
}