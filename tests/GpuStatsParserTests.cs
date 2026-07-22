using System.Linq;
using LlamaServerLauncher.Models;

public static class GpuStatsParserTests
{
    public static void Run(Harness h)
    {
        h.Section("GpuStatsParser");

        var single = GpuStatsParser.Parse("0, NVIDIA GeForce RTX 4090, 24564, 8192, 45, 62");
        h.Check("single count", single.Count == 1, single.Count.ToString());
        h.Check("single index", single.Count == 1 && single[0].Index == 0, single.FirstOrDefault()?.Index.ToString() ?? "null");
        h.Check("single name", single.Count == 1 && single[0].Name == "NVIDIA GeForce RTX 4090", single.FirstOrDefault()?.Name ?? "null");
        h.Check("single total", single.Count == 1 && single[0].MemTotalMb == 24564, single.FirstOrDefault()?.MemTotalMb?.ToString() ?? "null");
        h.Check("single used", single.Count == 1 && single[0].MemUsedMb == 8192, single.FirstOrDefault()?.MemUsedMb?.ToString() ?? "null");
        h.Check("single util", single.Count == 1 && single[0].UtilPercent == 45, single.FirstOrDefault()?.UtilPercent?.ToString() ?? "null");
        h.Check("single temp", single.Count == 1 && single[0].TempC == 62, single.FirstOrDefault()?.TempC?.ToString() ?? "null");

        var multi = GpuStatsParser.Parse("0, GPU A, 16000, 1000, 10, 50\n1, GPU B, 24000, 2000, 20, 55");
        h.Check("multi count", multi.Count == 2, multi.Count.ToString());
        h.Check("multi index1", multi.Count == 2 && multi[1].Index == 1, multi.Count == 2 ? multi[1].Index.ToString() : "n/a");
        h.Check("multi used1", multi.Count == 2 && multi[1].MemUsedMb == 2000, multi.Count == 2 ? multi[1].MemUsedMb?.ToString() ?? "null" : "n/a");

        var comma = GpuStatsParser.Parse("0, Fancy, GPU, 16000, 1000, 10, 50");
        h.Check("name with comma", comma.Count == 1 && comma[0].Name == "Fancy, GPU", comma.FirstOrDefault()?.Name ?? "null");
        h.Check("name with comma total", comma.Count == 1 && comma[0].MemTotalMb == 16000, comma.FirstOrDefault()?.MemTotalMb?.ToString() ?? "null");

        var na = GpuStatsParser.Parse("0, GPU, 8192, [N/A], [N/A], 55");
        h.Check("na used null", na.Count == 1 && na[0].MemUsedMb == null, na.FirstOrDefault()?.MemUsedMb?.ToString() ?? "null");
        h.Check("na util null", na.Count == 1 && na[0].UtilPercent == null, na.FirstOrDefault()?.UtilPercent?.ToString() ?? "null");
        h.Check("na temp present", na.Count == 1 && na[0].TempC == 55, na.FirstOrDefault()?.TempC?.ToString() ?? "null");

        h.Check("empty -> none", GpuStatsParser.Parse("").Count == 0, "ok");
        h.Check("null -> none", GpuStatsParser.Parse(null).Count == 0, "ok");
        h.Check("whitespace -> none", GpuStatsParser.Parse("   \n  ").Count == 0, "ok");
        h.Check("non-numeric index skipped", GpuStatsParser.Parse("index, name, total, used, util, temp").Count == 0, "ok");
        h.Check("too few fields skipped", GpuStatsParser.Parse("0, GPU, 100").Count == 0, "ok");
        h.Check("crlf handled", GpuStatsParser.Parse("0, GPU, 100, 50, 5, 40\r\n1, GPU2, 200, 60, 6, 41").Count == 2, "ok");
    }
}
