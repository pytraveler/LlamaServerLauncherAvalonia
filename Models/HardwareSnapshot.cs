using System;
using System.Collections.Generic;

namespace LlamaServerLauncher.Models;

public sealed record HardwareSnapshot(
    double? CpuPercent,
    double? RamPercent,
    double? RamUsedGb,
    double? RamTotalGb,
    IReadOnlyList<GpuInfo> Gpus)
{
    public static HardwareSnapshot Empty { get; } =
        new(null, null, null, null, Array.Empty<GpuInfo>());
}
