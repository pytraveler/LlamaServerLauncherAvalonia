using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace LlamaServerLauncher.Models;

public static class AmdGpuParser
{
    public static List<GpuInfo> Parse(string? json)
    {
        var list = new List<GpuInfo>();
        if (string.IsNullOrWhiteSpace(json)) return list;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return list; }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return list;

            foreach (var card in doc.RootElement.EnumerateObject())
            {
                if (!card.Name.StartsWith("card", StringComparison.OrdinalIgnoreCase)) continue;
                if (card.Value.ValueKind != JsonValueKind.Object) continue;

                int? util = null, temp = null, totalMb = null, usedMb = null;
                var name = card.Name;

                foreach (var p in card.Value.EnumerateObject())
                {
                    var key = p.Name.ToLowerInvariant();
                    var val = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString();
                    if (string.IsNullOrWhiteSpace(val)) continue;

                    if (util == null && key.Contains("gpu use") && !key.Contains("mem"))
                        util = ParseLeadingInt(val);
                    else if (key.Contains("vram") && key.Contains("used"))
                        usedMb = BytesToMb(val);
                    else if (key.Contains("vram") && key.Contains("total") && !key.Contains("used"))
                        totalMb = BytesToMb(val);
                    else if (temp == null && key.Contains("temperature") && key.Contains("edge"))
                        temp = ParseRoundedInt(val);
                    else if (key.Contains("series") || key.Contains("model") || key.Contains("product"))
                        name = val.Trim();
                }

                list.Add(new GpuInfo(ExtractIndex(card.Name), name, usedMb, totalMb, util, temp));
            }
        }

        return list;
    }

    private static int ExtractIndex(string cardName)
    {
        var digits = "";
        foreach (var c in cardName)
            if (char.IsDigit(c)) digits += c;
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static int? ParseLeadingInt(string value)
    {
        var s = value.Trim();
        var end = 0;
        while (end < s.Length && (char.IsDigit(s[end]) || s[end] == '.' || s[end] == '-')) end++;
        if (end == 0) return null;
        return double.TryParse(s[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? (int)Math.Round(d) : null;
    }

    private static int? ParseRoundedInt(string value) => ParseLeadingInt(value);

    private static int? BytesToMb(string value)
    {
        var s = value.Trim();
        return ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes)
            ? (int)Math.Min(bytes / (1024UL * 1024UL), int.MaxValue) : null;
    }
}
