using System;
using System.Collections.Generic;
using System.IO;

namespace LlamaServerLauncher.Models;

public sealed class ReferencedPath
{
    public string LabelKey { get; init; } = "";

    public string Label { get; init; } = "";

    public string Path { get; init; } = "";

    public bool IsDirectory { get; init; }
}

public sealed class ReferencedPathProbe
{
    public Func<string, bool> FileExists { get; init; } = File.Exists;

    public Func<string, bool> DirectoryExists { get; init; } = Directory.Exists;

    public Func<string, string?>? ExecutableResolver { get; init; }
}

public static class ReferencedPathScanner
{
    private static readonly string[] ExtraPathFlags =
    {
        "--chat-template-file",
        "--grammar-file",
        "--lora",
        "--api-key-file",
        "--ssl-key-file",
        "--ssl-cert-file",
        "--mcp-servers-config"
    };

    public static List<ReferencedPath> FindMissing(ServerConfiguration config, ReferencedPathProbe? probe = null)
    {
        var missing = new List<ReferencedPath>();
        if (config == null)
            return missing;

        if (config.RunInDocker)
            return missing;

        probe ??= new ReferencedPathProbe();
        var custom = ParseCustomArguments(config);

        if (!string.IsNullOrWhiteSpace(config.ExecutablePath) && !ExecutableExists(config.ExecutablePath, probe))
            missing.Add(new ReferencedPath { LabelKey = "LlamaServerExe", Path = config.ExecutablePath });

        AddIfFileMissing(missing, probe, "ModelM", CustomValueFor(custom, "ModelPath") ?? config.ModelPath);
        AddIfDirectoryMissing(missing, probe, "ModelsDir", CustomValueFor(custom, "ModelsDir") ?? config.ModelsDir);
        AddIfFileMissing(missing, probe, "MMProj", CustomValueFor(custom, "MmprojPath") ?? config.MmprojPath);
        AddIfFileMissing(missing, probe, "SpecDraftModel", CustomValueFor(custom, "SpecDraftModel") ?? config.SpecDraftModel);

        foreach (var flag in ExtraPathFlags)
        {
            if (!custom.TryGetValue(flag, out var value) || string.IsNullOrWhiteSpace(value))
                continue;

            if (!IsRooted(value))
                continue;

            if (!probe.FileExists(value))
                missing.Add(new ReferencedPath { Label = flag, Path = value });
        }

        return missing;
    }

    private static bool ExecutableExists(string path, ReferencedPathProbe probe)
    {
        if (probe.ExecutableResolver != null && !string.IsNullOrEmpty(probe.ExecutableResolver(path)))
            return true;

        return probe.FileExists(path);
    }

    private static void AddIfFileMissing(List<ReferencedPath> missing, ReferencedPathProbe probe, string labelKey, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || probe.FileExists(path))
            return;

        missing.Add(new ReferencedPath { LabelKey = labelKey, Path = path });
    }

    private static void AddIfDirectoryMissing(List<ReferencedPath> missing, ReferencedPathProbe probe, string labelKey, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || probe.DirectoryExists(path))
            return;

        missing.Add(new ReferencedPath { LabelKey = labelKey, Path = path, IsDirectory = true });
    }

    private static Dictionary<string, string?> ParseCustomArguments(ServerConfiguration config)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(config.CustomArguments))
            return values;

        try
        {
            var normalized = CommandLineParser.NormalizeSpecialCharacters(config.CustomArguments);
            var parsed = CommandLineParser.ParseArguments(normalized);
            var all = CommandLineParser.GetArgumentValues(parsed);

            foreach (var kvp in all)
            {
                if (config.CustomArgumentToggleStates != null
                    && config.CustomArgumentToggleStates.TryGetValue(kvp.Key, out var enabled)
                    && !enabled)
                {
                    continue;
                }

                values[kvp.Key] = kvp.Value;
            }
        }
        catch
        {
            // path check: fall back to the fields
        }

        return values;
    }

    private static string? CustomValueFor(Dictionary<string, string?> custom, string propertyName)
    {
        if (custom.Count == 0)
            return null;

        foreach (var kvp in ServerConfiguration.KnownArguments)
        {
            if (!kvp.Value.PropertyName.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (custom.TryGetValue(kvp.Key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static bool IsRooted(string path)
    {
        try { return System.IO.Path.IsPathRooted(path); }
        catch { return false; }
    }
}
