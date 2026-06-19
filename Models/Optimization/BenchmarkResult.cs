using System;

namespace LlamaServerLauncher.Models.Optimization;

public sealed class BenchmarkResult
{
    public double TgTs { get; init; }

    public double PpTs { get; init; }

    public double TgStddev { get; init; }
    public double PpStddev { get; init; }

    public int? GpuLayers { get; init; }

    public string RawCsv { get; init; } = "";

    public double MetricValue(BenchMetric metric) => metric switch
    {
        BenchMetric.Tg => TgTs,
        BenchMetric.Pp => PpTs,
        BenchMetric.Mean => (TgTs + PpTs) / 2.0,
        _ => 0.0,
    };

    public double MetricStddev(BenchMetric metric) => metric switch
    {
        BenchMetric.Tg => TgStddev,
        BenchMetric.Pp => PpStddev,
        BenchMetric.Mean => Math.Sqrt(TgStddev * TgStddev + PpStddev * PpStddev),
        _ => 0.0,
    };
}
