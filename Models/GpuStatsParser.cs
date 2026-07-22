using System.Collections.Generic;
using System.Globalization;

namespace LlamaServerLauncher.Models;

public sealed record GpuInfo(int Index, string Name, int? MemUsedMb, int? MemTotalMb, int? UtilPercent, int? TempC);

public static class GpuStatsParser
{
    public static List<GpuInfo> Parse(string? output)
    {
        var list = new List<GpuInfo>();
        if (string.IsNullOrWhiteSpace(output)) return list;

        foreach (var raw in output.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            var parts = line.Split(',');
            if (parts.Length < 6) continue;

            if (ParseInt(parts[0]) is not int index) continue;

            var n = parts.Length;
            var total = ParseInt(parts[n - 4]);
            var used = ParseInt(parts[n - 3]);
            var util = ParseInt(parts[n - 2]);
            var temp = ParseInt(parts[n - 1]);

            var name = string.Join(",", parts[1..(n - 4)]).Trim();

            list.Add(new GpuInfo(index, name, used, total, util, temp));
        }

        return list;
    }

    private static int? ParseInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var s = value.Trim();
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
