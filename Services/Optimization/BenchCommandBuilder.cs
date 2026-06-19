using System.Collections.Generic;

namespace LlamaServerLauncher.Services.Optimization;

public static class BenchCommandBuilder
{
    public const int CompareNGen = 128;
    public const int CompareNPrompt = 256;
    public const int CompareRepeat = 6;

    public static string BuildServer(
        string serverExe, string modelPath, int threads, int batch, int ubatch, int? ngl,
        bool? flash, string? overridePattern, int? ctxSize, int? nCpuMoe = null)
    {
        var sb = new List<string>
        {
            Q(serverExe), "--model", Q(modelPath),
            "-t", threads.ToString(),
            "--batch-size", batch.ToString(),
            "--ubatch-size", ubatch.ToString(),
        };
        if (ngl is { } n) { sb.Add("-ngl"); sb.Add(n.ToString()); }
        if (nCpuMoe is { } ncmoe) { sb.Add("--n-cpu-moe"); sb.Add(ncmoe.ToString()); }
        if (ctxSize is { } c) { sb.Add("--ctx-size"); sb.Add(c.ToString()); }
        if (flash == true) { sb.Add("--flash-attn"); sb.Add("on"); }
        if (!string.IsNullOrEmpty(overridePattern)) { sb.Add("--override-tensor"); sb.Add(QuoteAlways(overridePattern!)); }
        return string.Join(' ', sb);
    }

    public static string BuildBench(
        string benchExe, string modelPath, int threads, int batch, int ubatch, int? ngl,
        bool? flash, string? overridePattern, FlashAttnStyle flashStyle, bool supportsNoWarmup, int? nCpuMoe = null)
    {
        var sb = new List<string>
        {
            Q(benchExe), "--model", Q(modelPath),
            "-t", threads.ToString(),
            "--batch-size", batch.ToString(),
            "--ubatch-size", ubatch.ToString(),
        };
        if (ngl is { } n) { sb.Add("-ngl"); sb.Add(n.ToString()); }
        if (nCpuMoe is { } ncmoe) { sb.Add("--n-cpu-moe"); sb.Add(ncmoe.ToString()); }
        if (flash is { } fa)
        {
            sb.Add("--flash-attn");
            sb.Add(flashStyle == FlashAttnStyle.OnOff ? (fa ? "on" : "off") : (fa ? "1" : "0"));
        }
        sb.Add("-n"); sb.Add(CompareNGen.ToString());
        sb.Add("-p"); sb.Add(CompareNPrompt.ToString());
        sb.Add("-r"); sb.Add(CompareRepeat.ToString());
        if (supportsNoWarmup) sb.Add("--no-warmup");
        if (!string.IsNullOrEmpty(overridePattern)) { sb.Add("--override-tensor"); sb.Add(QuoteAlways(overridePattern!)); }
        return string.Join(' ', sb);
    }

    public static string BuildDefaultBench(string benchExe, string modelPath, bool supportsNoWarmup)
    {
        var sb = new List<string>
        {
            Q(benchExe), "--model", Q(modelPath),
            "-n", CompareNGen.ToString(),
            "-p", CompareNPrompt.ToString(),
            "-r", CompareRepeat.ToString(),
        };
        if (supportsNoWarmup) sb.Add("--no-warmup");
        return string.Join(' ', sb);
    }

    private static string Q(string s) => s.Contains(' ') ? $"\"{s}\"" : s;

    private static string QuoteAlways(string s) => $"\"{s}\"";
}
