using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using LlamaServerLauncher.Services;

namespace LlamaServerLauncher.Models;

public sealed class ModelScanEntry
{
    public string FullPath { get; init; } = "";
    public string FileName { get; init; } = "";
    public string RelativeDir { get; init; } = "";
    public long SizeBytes { get; init; }
    public GgufModelInfo? Info { get; init; }

    public string SizeText => ModelScanFormatting.FormatSize(SizeBytes);
    public string MetaText => ModelScanFormatting.BuildMeta(Info);
    public bool HasMeta => !string.IsNullOrEmpty(MetaText);
    public bool IsProjector => Info?.IsProjector == true;
    public bool HasRelativeDir => !string.IsNullOrEmpty(RelativeDir);
    public string DisplayName => HasRelativeDir ? RelativeDir + "/" + FileName : FileName;
}

public static class ModelScanFormatting
{
    private static readonly Regex ShardRegex =
        new(@"-(\d{5})-of-\d{5}\.gguf$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double val = bytes;
        int u = 0;
        while (val >= 1024 && u < units.Length - 1) { val /= 1024; u++; }
        string num = u == 0
            ? val.ToString("0", CultureInfo.InvariantCulture)
            : val.ToString("0.0", CultureInfo.InvariantCulture);
        return num + " " + units[u];
    }

    public static bool IsNonFirstShard(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var m = ShardRegex.Match(fileName);
        return m.Success && m.Groups[1].Value != "00001";
    }

    public static string BuildMeta(GgufModelInfo? info)
    {
        if (info == null) return "";
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(info.Quant)) parts.Add(info.Quant!);
        if (!string.IsNullOrEmpty(info.SizeLabel)) parts.Add(info.SizeLabel!);
        if (info.IsMoe) parts.Add(info.ExpertCount is int e ? "MoE·" + e : "MoE");
        if (info.MaxContext is int c) parts.Add("ctx " + c.ToString(CultureInfo.InvariantCulture));
        if (info.HasVision) parts.Add("vision");
        if (info.IsProjector) parts.Add("projector");
        return string.Join(" · ", parts);
    }
}
