using System.Linq;
using LlamaServerLauncher.Models;

public static class AmdGpuParserTests
{
    public static void Run(Harness h)
    {
        h.Section("AmdGpuParser");

        const string full = @"{
          ""card0"": {
            ""GPU use (%)"": ""3"",
            ""VRAM Total Memory (B)"": ""17163091968"",
            ""VRAM Total Used Memory (B)"": ""1610612736"",
            ""Temperature (Sensor edge) (C)"": ""42.0"",
            ""Card series"": ""Radeon RX 7900 XTX""
          }
        }";
        var g = AmdGpuParser.Parse(full);
        h.Check("full count", g.Count == 1, g.Count.ToString());
        h.Check("full index", g.Count == 1 && g[0].Index == 0, g.FirstOrDefault()?.Index.ToString() ?? "null");
        h.Check("full name", g.Count == 1 && g[0].Name == "Radeon RX 7900 XTX", g.FirstOrDefault()?.Name ?? "null");
        h.Check("full util", g.Count == 1 && g[0].UtilPercent == 3, g.FirstOrDefault()?.UtilPercent?.ToString() ?? "null");
        h.Check("full total MB", g.Count == 1 && g[0].MemTotalMb == 16368, g.FirstOrDefault()?.MemTotalMb?.ToString() ?? "null");
        h.Check("full used MB", g.Count == 1 && g[0].MemUsedMb == 1536, g.FirstOrDefault()?.MemUsedMb?.ToString() ?? "null");
        h.Check("full temp", g.Count == 1 && g[0].TempC == 42, g.FirstOrDefault()?.TempC?.ToString() ?? "null");

        const string multi = @"{
          ""card0"": { ""GPU use (%)"": ""10"" },
          ""card1"": { ""GPU use (%)"": ""20"" }
        }";
        var m = AmdGpuParser.Parse(multi);
        h.Check("multi count", m.Count == 2, m.Count.ToString());
        h.Check("multi index1", m.Count == 2 && m[1].Index == 1, m.Count == 2 ? m[1].Index.ToString() : "n/a");
        h.Check("multi util1", m.Count == 2 && m[1].UtilPercent == 20, m.Count == 2 ? m[1].UtilPercent?.ToString() ?? "null" : "n/a");
        h.Check("multi missing vram null", m.Count == 2 && m[0].MemTotalMb == null, m.FirstOrDefault()?.MemTotalMb?.ToString() ?? "null");

        const string withSystem = @"{
          ""system"": { ""Driver version"": ""6.0.0"" },
          ""card0"": { ""GPU use (%)"": ""5"" }
        }";
        var s = AmdGpuParser.Parse(withSystem);
        h.Check("ignores non-card keys", s.Count == 1 && s[0].UtilPercent == 5, s.Count.ToString());

        var na = AmdGpuParser.Parse(@"{ ""card0"": { ""GPU use (%)"": ""N/A"" } }");
        h.Check("util N/A -> null", na.Count == 1 && na[0].UtilPercent == null, na.FirstOrDefault()?.UtilPercent?.ToString() ?? "null");

        h.Check("empty -> none", AmdGpuParser.Parse("").Count == 0, "ok");
        h.Check("null -> none", AmdGpuParser.Parse(null).Count == 0, "ok");
        h.Check("invalid json -> none", AmdGpuParser.Parse("not json").Count == 0, "ok");
        h.Check("array json -> none", AmdGpuParser.Parse("[1,2,3]").Count == 0, "ok");
    }
}
