using System;
using System.Collections.Generic;
using System.Globalization;
using LlamaServerLauncher.Models;

namespace LlamaServerLauncher.Services;

public static class VramComparison
{
    public static string Describe(string profile, ServerMemoryReport report, VramEstimate? estimate)
    {
        var measured = $"weights {Gb(report.WeightBytes)} | cache {Gb(report.CacheBytes)}"
            + $" | compute {Gb(report.ComputeBytes)}";
        if (report.UnaccountedBytes > 0)
            measured += $" | headroom {Gb(report.UnaccountedBytes)}";
        if (report.HasLayers)
            measured += $" | layers {report.OffloadedLayers}/{report.TotalLayers}";

        var parts = new List<string>
        {
            $"VRAM for '{profile}': llama-server took {Gb(report.TotalBytes)} GB on the card ({measured})"
        };

        if (report.HostBytes > 0)
            parts.Add($"plus {Gb(report.HostBytes)} GB in system memory");

        if (report.DeviceCount > 1)
            parts.Add($"spread over {report.DeviceCount} devices");

        if (estimate == null)
        {
            parts.Add("nothing to compare it against");
            return string.Join("; ", parts);
        }

        long delta = estimate.TotalBytes - report.TotalBytes;
        parts.Add($"the estimate said {Gb(estimate.TotalBytes)} GB"
            + $" (weights {Gb(estimate.WeightBytes)} | cache {Gb(estimate.KvBytes)}"
            + $" | compute {Gb(estimate.ComputeBytes)} | headroom {Gb(estimate.OverheadBytes)})"
            + $", {Gb(Math.Abs(delta))} GB {(delta >= 0 ? "over" : "under")}");

        return string.Join("; ", parts);
    }

    private static string Gb(long bytes) =>
        (bytes / 1024.0 / 1024.0 / 1024.0).ToString("F2", CultureInfo.InvariantCulture);
}
