using System;
using LlamaServerLauncher.Models;

public static class CpuUsageTests
{
    public static void Run(Harness h)
    {
        h.Section("CpuUsage.Percent");

        h.Check("75% busy", Approx(CpuUsage.Percent(100, 200, 150, 400), 75), CpuUsage.Percent(100, 200, 150, 400).ToString("0.0"));
        h.Check("all idle -> 0", Approx(CpuUsage.Percent(100, 200, 300, 400), 0), CpuUsage.Percent(100, 200, 300, 400).ToString("0.0"));
        h.Check("fully busy -> 100", Approx(CpuUsage.Percent(100, 200, 100, 400), 100), CpuUsage.Percent(100, 200, 100, 400).ToString("0.0"));
        h.Check("no change -> 0", Approx(CpuUsage.Percent(100, 200, 100, 200), 0), CpuUsage.Percent(100, 200, 100, 200).ToString("0.0"));
        h.Check("counter reset -> 0", Approx(CpuUsage.Percent(100, 500, 50, 200), 0), CpuUsage.Percent(100, 500, 50, 200).ToString("0.0"));
        h.Check("never above 100", CpuUsage.Percent(100, 200, 90, 400) <= 100, CpuUsage.Percent(100, 200, 90, 400).ToString("0.0"));
        h.Check("never below 0", CpuUsage.Percent(0, 0, 0, 0) >= 0, CpuUsage.Percent(0, 0, 0, 0).ToString("0.0"));
    }

    private static bool Approx(double a, double b) => Math.Abs(a - b) < 0.01;
}
