using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace LlamaServerLauncher.Models;

public enum InferenceStatKind
{
    Prompt,
    Gen
}

public readonly record struct InferenceStat(InferenceStatKind Kind, double TokensPerSecond);

public static class InferenceStatsParser
{
    private static readonly Regex TpsRegex =
        new(@"([0-9]+(?:\.[0-9]+)?)\s*tokens per second", RegexOptions.Compiled);

    public static InferenceStat? TryParse(string? line)
    {
        if (string.IsNullOrEmpty(line)) return null;
        if (line.IndexOf("tokens per second", StringComparison.Ordinal) < 0) return null;

        var m = TpsRegex.Match(line);
        if (!m.Success) return null;
        if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var tps))
            return null;

        if (line.IndexOf("prompt eval time", StringComparison.Ordinal) >= 0)
            return new InferenceStat(InferenceStatKind.Prompt, tps);
        if (line.IndexOf("eval time", StringComparison.Ordinal) >= 0)
            return new InferenceStat(InferenceStatKind.Gen, tps);

        return null;
    }
}
