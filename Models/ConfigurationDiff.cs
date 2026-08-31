using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace LlamaServerLauncher.Models;

public sealed class ConfigFieldSpec
{
    public string PropertyName { get; init; } = string.Empty;

    public string GroupKey { get; init; } = string.Empty;

    public string LabelKey { get; init; } = string.Empty;

    public string[] Companions { get; init; } = Array.Empty<string>();
}

public sealed class ConfigChange
{
    public string PropertyName { get; init; } = string.Empty;
    public string GroupKey { get; init; } = string.Empty;
    public string LabelKey { get; init; } = string.Empty;

    public string Flag { get; init; } = string.Empty;

    public object? OldValue { get; init; }
    public object? NewValue { get; init; }

    public bool Apply { get; init; }

    public bool ClearsValue => IsUnset(NewValue) && !IsUnset(OldValue);

    internal static bool IsUnset(object? value) => value switch
    {
        null => true,
        string s => string.IsNullOrEmpty(s),
        ICollection c => c.Count == 0,
        _ => false
    };
}

public static class ConfigurationDiff
{
    public const string GroupMain = "TabMain";
    public const string GroupCustom = "TabCustom";
    public const string GroupGeneration = "TabGeneration";
    public const string GroupOptions = "TabOptions";
    public const string GroupSpeculative = "TabSpeculative";
    public const string GroupDocker = "TabDocker";
    public const string GroupMcp = "TabMcp";

    public static readonly IReadOnlyList<ConfigFieldSpec> Fields = new List<ConfigFieldSpec>
    {
        new() { PropertyName = "ExecutablePath", GroupKey = GroupMain, LabelKey = "LlamaServerExe" },
        new() { PropertyName = "ModelPath", GroupKey = GroupMain, LabelKey = "ModelM" },
        new() { PropertyName = "ModelsDir", GroupKey = GroupMain, LabelKey = "ModelsDir" },
        new() { PropertyName = "MmprojPath", GroupKey = GroupMain, LabelKey = "MMProj" },
        new() { PropertyName = "HfRepo", GroupKey = GroupMain, LabelKey = "HfRepo" },
        new() { PropertyName = "HfFile", GroupKey = GroupMain, LabelKey = "HfFile" },
        new() { PropertyName = "HfRepoDraft", GroupKey = GroupMain, LabelKey = "HfRepoDraft" },
        new() { PropertyName = "Offline", GroupKey = GroupMain, LabelKey = "Offline" },
        new() { PropertyName = "Host", GroupKey = GroupMain, LabelKey = "Host" },
        new() { PropertyName = "Port", GroupKey = GroupMain, LabelKey = "Port" },
        new() { PropertyName = "Alias", GroupKey = GroupMain, LabelKey = "Alias" },
        new() { PropertyName = "ApiKey", GroupKey = GroupMain, LabelKey = "ApiKey" },
        new() { PropertyName = "LogFilePath", GroupKey = GroupMain, LabelKey = "LogFile" },
        new() { PropertyName = "VerboseLogging", GroupKey = GroupMain, LabelKey = "VerboseLogging" },

        new()
        {
            PropertyName = "CustomArguments",
            GroupKey = GroupCustom,
            LabelKey = "CustomArguments",
            Companions = new[] { "CustomArgumentToggleStates" }
        },

        new() { PropertyName = "ContextSize", GroupKey = GroupGeneration, LabelKey = "ContextSize" },
        new() { PropertyName = "Temperature", GroupKey = GroupGeneration, LabelKey = "Temperature" },
        new() { PropertyName = "MaxTokens", GroupKey = GroupGeneration, LabelKey = "MaxTokens" },
        new() { PropertyName = "TopK", GroupKey = GroupGeneration, LabelKey = "TopK" },
        new() { PropertyName = "TopP", GroupKey = GroupGeneration, LabelKey = "TopP" },
        new() { PropertyName = "MinP", GroupKey = GroupGeneration, LabelKey = "MinP" },
        new() { PropertyName = "RepeatPenalty", GroupKey = GroupGeneration, LabelKey = "RepeatPenalty" },
        new() { PropertyName = "PresencePenalty", GroupKey = GroupGeneration, LabelKey = "PresencePenalty" },
        new() { PropertyName = "FrequencyPenalty", GroupKey = GroupGeneration, LabelKey = "FrequencyPenalty" },
        new() { PropertyName = "Seed", GroupKey = GroupGeneration, LabelKey = "Seed" },
        new() { PropertyName = "Reasoning", GroupKey = GroupGeneration, LabelKey = "Reasoning" },
        new() { PropertyName = "ReasoningBudget", GroupKey = GroupGeneration, LabelKey = "ReasoningBudget" },
        new() { PropertyName = "CachePrompt", GroupKey = GroupGeneration, LabelKey = "CachePrompt" },
        new() { PropertyName = "ContextShift", GroupKey = GroupGeneration, LabelKey = "ContextShift" },

        new() { PropertyName = "Threads", GroupKey = GroupOptions, LabelKey = "Threads" },
        new() { PropertyName = "GpuLayers", GroupKey = GroupOptions, LabelKey = "GpuLayers" },
        new() { PropertyName = "CpuMoe", GroupKey = GroupOptions, LabelKey = "CpuMoe" },
        new() { PropertyName = "BatchSize", GroupKey = GroupOptions, LabelKey = "BatchSize" },
        new() { PropertyName = "UBatchSize", GroupKey = GroupOptions, LabelKey = "UBatchSize" },
        new() { PropertyName = "CacheTypeK", GroupKey = GroupOptions, LabelKey = "CacheTypeK" },
        new() { PropertyName = "CacheTypeV", GroupKey = GroupOptions, LabelKey = "CacheTypeV" },
        new() { PropertyName = "ParallelSlots", GroupKey = GroupOptions, LabelKey = "ParallelSlots" },
        new() { PropertyName = "Timeout", GroupKey = GroupOptions, LabelKey = "Timeout" },
        new() { PropertyName = "FlashAttention", GroupKey = GroupOptions, LabelKey = "FlashAttention" },
        new() { PropertyName = "EnableWebUI", GroupKey = GroupOptions, LabelKey = "WebUI" },
        new() { PropertyName = "EmbeddingMode", GroupKey = GroupOptions, LabelKey = "Embedding" },
        new() { PropertyName = "EnableSlots", GroupKey = GroupOptions, LabelKey = "Slots" },
        new() { PropertyName = "EnableMetrics", GroupKey = GroupOptions, LabelKey = "Metrics" },
        new() { PropertyName = "ContBatching", GroupKey = GroupOptions, LabelKey = "ContBatching" },
        new() { PropertyName = "Mlock", GroupKey = GroupOptions, LabelKey = "Mlock" },
        new() { PropertyName = "Mmap", GroupKey = GroupOptions, LabelKey = "Mmap" },

        new() { PropertyName = "SpecType", GroupKey = GroupSpeculative, LabelKey = "SpecType" },
        new() { PropertyName = "SpecDraftModel", GroupKey = GroupSpeculative, LabelKey = "SpecDraftModel" },
        new() { PropertyName = "SpecDraftGpuLayers", GroupKey = GroupSpeculative, LabelKey = "SpecDraftGpuLayers" },
        new() { PropertyName = "SpecDraftNMax", GroupKey = GroupSpeculative, LabelKey = "SpecDraftNMax" },
        new() { PropertyName = "SpecDraftNMin", GroupKey = GroupSpeculative, LabelKey = "SpecDraftNMin" },
        new() { PropertyName = "SpecDraftPSplit", GroupKey = GroupSpeculative, LabelKey = "SpecDraftPSplit" },
        new() { PropertyName = "SpecDraftPMin", GroupKey = GroupSpeculative, LabelKey = "SpecDraftPMin" },

        new() { PropertyName = "RunInDocker", GroupKey = GroupDocker, LabelKey = "RunInDocker" },
        new() { PropertyName = "DockerImage", GroupKey = GroupDocker, LabelKey = "DockerImage" },
        new() { PropertyName = "DockerGpuAll", GroupKey = GroupDocker, LabelKey = "DockerUseAllGpus" },
        new() { PropertyName = "DockerRm", GroupKey = GroupDocker, LabelKey = "DockerRemoveOnStop" },
        new() { PropertyName = "DockerContainerName", GroupKey = GroupDocker, LabelKey = "DockerContainerName" },

        new() { PropertyName = "McpEnabled", GroupKey = GroupMcp, LabelKey = "McpEnabled" },
        new() { PropertyName = "McpServers", GroupKey = GroupMcp, LabelKey = "ImportFieldMcpServers" }
    };

    private static readonly HashSet<string> NotImported = new(StringComparer.Ordinal)
    {
        "McpConfigPath"
    };

    public static List<ConfigChange> Build(
        ServerConfiguration current,
        ServerConfiguration incoming,
        ISet<string>? mentioned = null)
    {
        var changes = new List<ConfigChange>();
        if (current == null || incoming == null)
            return changes;

        foreach (var field in Fields)
        {
            var property = typeof(ServerConfiguration).GetProperty(field.PropertyName);
            if (property == null)
                continue;

            var oldValue = property.GetValue(current);
            var newValue = property.GetValue(incoming);
            if (SameValue(oldValue, newValue))
                continue;

            changes.Add(new ConfigChange
            {
                PropertyName = field.PropertyName,
                GroupKey = field.GroupKey,
                LabelKey = field.LabelKey,
                Flag = CanonicalFlag(field.PropertyName),
                OldValue = oldValue,
                NewValue = newValue,
                Apply = mentioned == null || mentioned.Contains(field.PropertyName)
            });
        }

        return changes;
    }

    public static ServerConfiguration Merge(
        ServerConfiguration current,
        ServerConfiguration incoming,
        IEnumerable<string> propertyNames)
    {
        var merged = current.Clone();
        if (propertyNames == null)
            return merged;

        var wanted = new HashSet<string>(propertyNames, StringComparer.Ordinal);
        foreach (var field in Fields)
        {
            if (!wanted.Contains(field.PropertyName))
                continue;

            Copy(incoming, merged, field.PropertyName);
            foreach (var companion in field.Companions)
                Copy(incoming, merged, companion);
        }

        return merged;
    }

    public static HashSet<string> PropertiesMentionedIn(IEnumerable<string>? tokens)
    {
        var mentioned = new HashSet<string>(StringComparer.Ordinal);
        if (tokens == null)
            return mentioned;

        foreach (var token in tokens)
        {
            if (!CommandLineParser.IsFlag(token))
                continue;

            if (ServerConfiguration.KnownArguments.TryGetValue(token, out var mapping))
                mentioned.Add(mapping.PropertyName);
            else
                mentioned.Add("CustomArguments");
        }

        return mentioned;
    }

    public static string? Describe(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case string s:
                return string.IsNullOrEmpty(s) ? null : s;
            case bool b:
                return b ? "on" : "off";
            case double d:
                return d.ToString(CultureInfo.InvariantCulture);
            case IFormattable f:
                return f.ToString(null, CultureInfo.InvariantCulture);
            case ICollection c:
                return c.Count == 0 ? null : c.Count.ToString(CultureInfo.InvariantCulture);
            default:
                return value.ToString();
        }
    }

    public static List<string> UncoveredProperties()
    {
        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in Fields)
        {
            covered.Add(field.PropertyName);
            foreach (var companion in field.Companions)
                covered.Add(companion);
        }

        return typeof(ServerConfiguration)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .Select(p => p.Name)
            .Where(name => !covered.Contains(name) && !NotImported.Contains(name))
            .ToList();
    }

    public static string ComposeLabel(string? localizedName, string? flag)
    {
        var name = (localizedName ?? string.Empty).Trim().TrimEnd(':').TrimEnd();
        if (string.IsNullOrEmpty(flag) || name.Contains("(-", StringComparison.Ordinal))
            return name;

        return $"{name} ({flag})";
    }

    private static string CanonicalFlag(string propertyName)
    {
        var flags = ServerConfiguration.GetFlagsForProperty(propertyName);
        if (flags.Count == 0)
            return string.Empty;

        var plain = flags.Where(f => !IsNegated(f)).ToList();
        var candidates = plain.Count > 0 ? plain : flags;
        return candidates.OrderByDescending(f => f.Length).First();
    }

    private static bool IsNegated(string flag) =>
        flag.StartsWith("--no-", StringComparison.Ordinal) ||
        flag.StartsWith("-no", StringComparison.Ordinal);

    private static void Copy(ServerConfiguration from, ServerConfiguration to, string propertyName)
    {
        var property = typeof(ServerConfiguration).GetProperty(propertyName);
        if (property == null || !property.CanWrite)
            return;

        var value = property.GetValue(from);

        switch (value)
        {
            case List<McpServerEntry> servers:
                value = servers.Select(s => s.Clone()).ToList();
                break;
            case Dictionary<string, bool> toggles:
                value = new Dictionary<string, bool>(toggles);
                break;
        }

        property.SetValue(to, value);
    }

    private static bool SameValue(object? a, object? b)
    {
        if (ConfigChange.IsUnset(a) && ConfigChange.IsUnset(b))
            return true;

        if (a is List<McpServerEntry> first && b is List<McpServerEntry> second)
        {
            if (first.Count != second.Count)
                return false;

            for (int i = 0; i < first.Count; i++)
            {
                if (!first[i].SameAs(second[i]))
                    return false;
            }

            return true;
        }

        return Equals(a, b);
    }
}
