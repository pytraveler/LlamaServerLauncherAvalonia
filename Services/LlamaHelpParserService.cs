using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LlamaServerLauncher.Models;

namespace LlamaServerLauncher.Services;

public static class LlamaHelpParserService
{
    private static readonly Regex FlagPattern = new(
        @"(?:^|\s|,)(-[a-zA-Z](?:\s*,\s*|-+))(?=\s|,|$)|(--[\w][\w-]*)",
        RegexOptions.Compiled);

    private static readonly Regex AllFlagsPattern = new(
        @"(-[a-zA-Z][a-zA-Z]*\b)|(--[\w][\w-]*)",
        RegexOptions.Compiled);

    public static async Task<HashSet<string>?> GetSupportedFlagsAsync(string executablePath)
    {
        var result = await GetSupportedFlagsWithHelpAsync(executablePath);
        return result?.Flags;
    }

    public static async Task<HelpParseResult?> GetSupportedFlagsWithHelpAsync(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return null;

        if (!System.IO.File.Exists(executablePath))
            return null;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = "--help",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            // Read both streams in parallel to avoid deadlock when the process
            // writes enough to fill one buffer while we're awaiting the other.
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Timeout — kill the process and return null
                try { process.Kill(entireProcessTree: true); } catch { }
                return null;
            }

            var output = await outputTask;
            var error = await errorTask;

            var fullOutput = output + "\n" + error;

            if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(fullOutput))
                return null;

            var flags = ParseFlagsFromHelp(fullOutput);
            return new HelpParseResult { Flags = flags, HelpText = fullOutput };
        }
        catch
        {
            return null;
        }
    }

    public static HashSet<string> ParseFlagsFromHelp(string helpText)
    {
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(helpText))
            return flags;

        foreach (var line in helpText.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            if (!trimmed.StartsWith("-") && !trimmed.StartsWith("  -"))
                continue;

            var matches = AllFlagsPattern.Matches(trimmed);
            foreach (Match match in matches)
            {
                if (match.Success)
                {
                    var flag = match.Value;
                    if (flag.StartsWith("--") || flag.StartsWith("-"))
                    {
                        if (!IsExcludedFlag(flag))
                        {
                            flags.Add(flag);
                        }
                    }
                }
            }
        }

        return flags;
    }

    private static bool IsExcludedFlag(string flag)
    {
        switch (flag.ToLowerInvariant())
        {
            case "-h":
            case "--help":
            case "--usage":
            case "--version":
            case "--license":
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Extracts the valid enum-like values for a flag from help text.
    /// Looks for patterns like: --flag [val1|val2|val3] or --flag &lt;val1|val2|val3&gt;
    /// </summary>
    public static List<string> ParseFlagValues(string helpText, string flag)
    {
        var values = new List<string>();

        if (string.IsNullOrWhiteSpace(helpText) || string.IsNullOrWhiteSpace(flag))
            return values;

        // Match the flag followed by optional whitespace and a bracketed/angled list of values
        // Patterns: [val1|val2|val3] or <val1|val2|val3>
        var escapedFlag = Regex.Escape(flag);
        var pattern = escapedFlag + @"\s*(?:\[([^\]]+)\]|<([^>]+)>)";
        var match = Regex.Match(helpText, pattern, RegexOptions.IgnoreCase);

        if (!match.Success)
            return values;

        var capturedGroup = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        foreach (var val in capturedGroup.Split('|'))
        {
            var trimmed = val.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                values.Add(trimmed);
        }

        return values;
    }

    private static readonly Regex DescriptionFlagRegex = new(
        @"(-[a-zA-Z][a-zA-Z0-9]*\b)|(--[\w][\w-]*)",
        RegexOptions.Compiled);

    public static List<HelpArgumentInfo> ParseArgumentDescriptions(string helpText)
    {
        var result = new List<HelpArgumentInfo>();
        if (string.IsNullOrWhiteSpace(helpText))
            return result;

        var lines = helpText.Split('\n');
        HelpArgumentInfo? current = null;
        var descriptionLines = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("-"))
            {
                // Save previous argument
                if (current != null)
                {
                    FinalizeArgument(current, descriptionLines);
                    result.Add(current);
                }

                // Find boundary between flags/description
                int boundary = FindFlagsDescriptionBoundary(line);

                string flagsPart;
                string descPart;
                if (boundary >= 0 && boundary < line.Length)
                {
                    flagsPart = line.Substring(0, boundary).TrimEnd();
                    descPart = line.Substring(boundary).TrimStart();
                }
                else
                {
                    flagsPart = line.TrimEnd();
                    descPart = "";
                }

                var flagMatches = DescriptionFlagRegex.Matches(flagsPart);
                var allFlags = new List<string>();
                foreach (Match m in flagMatches)
                {
                    if (m.Success && !string.IsNullOrEmpty(m.Value))
                        allFlags.Add(m.Value);
                }

                string primaryFlag = "";
                foreach (var f in allFlags)
                {
                    if (f.StartsWith("--"))
                    {
                        primaryFlag = f;
                        break;
                    }
                }
                if (string.IsNullOrEmpty(primaryFlag) && allFlags.Count > 0)
                    primaryFlag = allFlags[0];

                current = new HelpArgumentInfo
                {
                    PrimaryFlag = primaryFlag,
                    AllFlags = allFlags
                };
                descriptionLines.Clear();
                if (!string.IsNullOrWhiteSpace(descPart))
                    descriptionLines.Add(descPart);
            }
            else if (current != null && !string.IsNullOrWhiteSpace(line))
            {
                // Continuation line — must be indented
                if (line.StartsWith(" ") || line.StartsWith("\t"))
                {
                    descriptionLines.Add(trimmed);
                }
                else
                {
                    // New section or separator — finalize current
                    FinalizeArgument(current, descriptionLines);
                    result.Add(current);
                    current = null;
                    descriptionLines.Clear();
                }
            }
        }

        if (current != null)
        {
            FinalizeArgument(current, descriptionLines);
            result.Add(current);
        }

        return result;
    }

    private static int FindFlagsDescriptionBoundary(string line)
    {
        // Primary: first double space or tab
        for (int i = 0; i < line.Length - 1; i++)
        {
            if ((line[i] == ' ' && line[i + 1] == ' ') || line[i] == '\t')
            {
                int j = i;
                while (j < line.Length && (line[j] == ' ' || line[j] == '\t'))
                    j++;
                return j;
            }
        }

        // Fallback: after last flag and its optional value placeholder
        var matches = DescriptionFlagRegex.Matches(line);
        if (matches.Count > 0)
        {
            var lastMatch = matches[^1];
            var afterLast = line.Substring(lastMatch.Index + lastMatch.Length);
            var trimmedAfter = afterLast.TrimStart();

            if (!trimmedAfter.StartsWith("-") && !string.IsNullOrWhiteSpace(trimmedAfter))
            {
                // Likely a value placeholder; find end of it
                var spaceIdx = trimmedAfter.IndexOf(' ');
                if (spaceIdx >= 0)
                {
                    return lastMatch.Index + lastMatch.Length + (afterLast.Length - trimmedAfter.Length) + spaceIdx + 1;
                }
            }
        }

        return -1;
    }

    private static void FinalizeArgument(HelpArgumentInfo arg, List<string> descriptionLines)
    {
        var desc = string.Join(" ", descriptionLines).Trim();
        var (def, _) = ExtractMeta(desc);

        // Strip meta markers from description for cleaner display
        desc = Regex.Replace(desc, @"\s*\(default:\s*[^)]+\)", "");
        desc = Regex.Replace(desc, @"\s*\(env:\s*[^)]+\)", "");
        desc = desc.Trim();

        arg.Description = desc;
        arg.DefaultValue = CleanDefaultValue(def);
    }

    private static string? CleanDefaultValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        // Normalize whitespace: collapse newlines/tabs/multiple spaces into single space
        value = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        while (value.Contains("  "))
            value = value.Replace("  ", " ");
        value = value.Trim();

        var lower = value.ToLowerInvariant();
        // Skip boolean-like defaults that are not meaningful argument values
        if (lower is "enabled" or "disabled" or "on" or "off" or "true" or "false" or "yes" or "no" or "auto" or "none")
            return null;

        // Quote multi-word values
        if (value.Contains(' '))
            return $"\"{value}\"";

        return value;
    }

    private static (string? defaultValue, string? envVar) ExtractMeta(string description)
    {
        string? def = null;
        string? env = null;

        var defMatch = Regex.Match(description, @"\(default:\s*([^)]+)\)");
        if (defMatch.Success)
            def = defMatch.Groups[1].Value.Trim();

        var envMatch = Regex.Match(description, @"\(env:\s*([^)]+)\)");
        if (envMatch.Success)
            env = envMatch.Groups[1].Value.Trim();

        return (def, env);
    }

    /// <summary>
    /// Extracts valid cache type values (for -ctk / -ctv / --cache-type-k / --cache-type-v)
    /// from help text. Handles:
    ///   - "allowed values: f32, f16, bf16, q8_0, ..." (may span multiple lines)
    ///   - [f32|f16|q8_0|...] or &lt;f32|f16|...&gt; bracket/angle patterns
    /// </summary>
    public static List<string> ParseCacheTypeValues(string helpText)
    {
        var values = new List<string>();

        if (string.IsNullOrWhiteSpace(helpText))
            return values;

        var lines = helpText.Split('\n');

        // First, try the "allowed values:" format (modern llama.cpp)
        // Format:
        //   -ctk,  --cache-type-k TYPE              KV cache data type for K
        //                                         allowed values: f32, f16, bf16, q8_0, q4_0, q4_1, iq4_nl, q5_0, q5_1
        //                                         (default: f16)
        // Values may continue on the next line (e.g. turbo2, turbo3, turbo4).
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (!line.Contains("cache-type-k", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("-ctk", StringComparison.OrdinalIgnoreCase))
                continue;

            // Scan subsequent lines for "allowed values:"
            for (int j = i; j < Math.Min(i + 6, lines.Length); j++)
            {
                var subLine = lines[j];
                var avIdx = subLine.IndexOf("allowed values:", StringComparison.OrdinalIgnoreCase);
                if (avIdx < 0)
                    continue;

                // Collect the comma-separated values, possibly spanning multiple lines
                var remaining = subLine.Substring(avIdx + "allowed values:".Length);
                var collected = remaining;

                // Continue on subsequent lines until we hit a line that starts a new section
                // (i.e. a non-indented line, or a line with "(default:", "(env:", etc.)
                for (int k = j + 1; k < Math.Min(j + 4, lines.Length); k++)
                {
                    var nextLine = lines[k].TrimStart();
                    if (string.IsNullOrEmpty(nextLine))
                        break;
                    if (nextLine.StartsWith("(") || nextLine.StartsWith("-") || nextLine.StartsWith("--"))
                        break;
                    collected += ", " + nextLine;
                }

                foreach (var val in collected.Split(','))
                {
                    var trimmed = val.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && trimmed != "..."
                        && !values.Contains(trimmed))
                        values.Add(trimmed);
                }

                return values; // found for -ctk, that's sufficient (same list for -ctv)
            }
        }

        // Fallback: try bracket/angle patterns on lines containing cache-type/-ctk/-ctv
        foreach (var line in lines)
        {
            if (!line.Contains("cache-type", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("-ctk", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("-ctv", StringComparison.OrdinalIgnoreCase))
                continue;

            // Pattern: [val1|val2|...]
            foreach (Match m in Regex.Matches(line, @"\[([^\]]+)\]"))
            {
                foreach (var val in m.Groups[1].Value.Split('|'))
                {
                    var trimmed = val.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && trimmed != "..."
                        && !values.Contains(trimmed))
                        values.Add(trimmed);
                }
            }

            // Pattern: <val1|val2|...>
            foreach (Match m in Regex.Matches(line, @"<([^>]+)>"))
            {
                foreach (var val in m.Groups[1].Value.Split('|'))
                {
                    var trimmed = val.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && trimmed != "..."
                        && !values.Contains(trimmed))
                        values.Add(trimmed);
                }
            }
        }

        return values;
    }

    /// <summary>
    /// Extracts valid --spec-type values from help text, handling both 
    /// "without draft model" and "with draft model" categories.
    /// Also adds "draft-simple" if -md flag is present in help.
    /// </summary>
    public static List<string> ParseSpecTypeValues(string helpText)
    {
        var values = new List<string>();

        if (string.IsNullOrWhiteSpace(helpText))
            return values;

        // Parse lines containing --spec-type
        foreach (var line in helpText.Split('\n'))
        {
            if (!line.Contains("--spec-type", StringComparison.OrdinalIgnoreCase))
                continue;

            // Pattern 1: [val1|val2|...] — newer llama.cpp format
            var bracketPattern = @"\[([^\]]+)\]";
            foreach (Match m in Regex.Matches(line, bracketPattern))
            {
                foreach (var val in m.Groups[1].Value.Split('|'))
                {
                    var trimmed = val.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && trimmed != "..."
                        && !values.Contains(trimmed))
                        values.Add(trimmed);
                }
            }

            // Pattern 2: <val1|val2|...>
            var anglePattern = @"<([^>]+)>";
            foreach (Match m in Regex.Matches(line, anglePattern))
            {
                foreach (var val in m.Groups[1].Value.Split('|'))
                {
                    var trimmed = val.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && trimmed != "..."
                        && !values.Contains(trimmed))
                        values.Add(trimmed);
                }
            }

            // Pattern 3: comma-separated values after --spec-type (e.g. "none,draft-simple,draft-mtp,...")
            // Newer versions list values like: --spec-type none,draft-simple,draft-mtp,...
            var commaMatch = Regex.Match(line, @"--spec-type\s+([a-zA-Z][\w,-]*(?:,[\w-]+)+)");
            if (commaMatch.Success)
            {
                foreach (var val in commaMatch.Groups[1].Value.Split(','))
                {
                    var trimmed = val.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && trimmed != "..."
                        && !values.Contains(trimmed))
                        values.Add(trimmed);
                }
            }
        }

        return values;
    }
}

public class HelpParseResult
{
    public HashSet<string> Flags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string HelpText { get; set; } = "";
}
