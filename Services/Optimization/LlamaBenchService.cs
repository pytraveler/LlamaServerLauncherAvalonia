using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LlamaServerLauncher.Models.Optimization;

namespace LlamaServerLauncher.Services.Optimization;

public sealed class ProcessRunResult
{
    public int ExitCode { get; init; }
    public string StdOut { get; init; } = "";
    public string StdErr { get; init; } = "";
    public bool TimedOut { get; init; }

    public bool Success => !TimedOut && ExitCode == 0;
}

public sealed class LlamaBenchService : IConfigBenchmarker
{
    private readonly string _benchExePath;
    private readonly LlamaBenchCapabilities _capabilities;
    private readonly LogService? _log;

    public LlamaBenchService(string benchExePath, LlamaBenchCapabilities capabilities, LogService? log = null)
    {
        _benchExePath = benchExePath;
        _capabilities = capabilities;
        _log = log;
    }

    public string BenchExePath => _benchExePath;
    public LlamaBenchCapabilities Capabilities => _capabilities;

    public static string? ResolveBenchPath(string? explicitBenchPath, string? llamaServerExecutablePath)
    {
        if (!string.IsNullOrWhiteSpace(explicitBenchPath))
        {
            var resolved = LlamaServerService.ResolveExecutablePath(explicitBenchPath);
            if (resolved != null) return resolved;
        }

        if (!string.IsNullOrWhiteSpace(llamaServerExecutablePath))
        {
            var serverResolved = LlamaServerService.ResolveExecutablePath(llamaServerExecutablePath);
            var dir = Path.GetDirectoryName(serverResolved ?? llamaServerExecutablePath);
            if (!string.IsNullOrEmpty(dir))
            {
                var name = OperatingSystem.IsWindows() ? "llama-bench.exe" : "llama-bench";
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
        }

        return LlamaServerService.ResolveExecutablePath("llama-bench");
    }

    public async Task<BenchmarkResult> RunBenchAsync(BenchArgs args, int timeoutSeconds, CancellationToken ct)
    {
        var argv = BuildArgs(args);
        var result = await RunRawAsync(argv, timeoutSeconds, ct);
        if (!result.Success)
        {
            var reason = result.TimedOut ? "timed out" : $"exited with code {result.ExitCode}";
            throw new InvalidOperationException(
                $"llama-bench {reason}. stderr: {Truncate(result.StdErr, 500)}");
        }
        return BenchCsvParser.Parse(result.StdOut);
    }

    public async Task<ProcessRunResult> RunRawAsync(IReadOnlyList<string> args, int timeoutSeconds, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _benchExePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        var sbOut = new StringBuilder();
        var sbErr = new StringBuilder();

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };

        _log?.Debug($"llama-bench: {_benchExePath} {string.Join(' ', args)}");

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        using var killReg = linked.Token.Register(() => KillTree(process));

        bool timedOut = false;
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            try { await process.WaitForExitAsync(CancellationToken.None); } catch {}

            if (ct.IsCancellationRequested)
                throw new OperationCanceledException(ct);
            timedOut = true;
        }


        try { process.WaitForExit(); } catch {}

        int exitCode = -1;
        try { exitCode = process.ExitCode; } catch {}

        var stdErr = sbErr.ToString();
        if (!timedOut && exitCode != 0 && stdErr.Length > 0)
            _log?.Debug($"llama-bench failed (exit {exitCode}). stderr tail: {Truncate(stdErr, 600)}");

        return new ProcessRunResult
        {
            ExitCode = exitCode,
            StdOut = sbOut.ToString(),
            StdErr = stdErr,
            TimedOut = timedOut,
        };
    }

    public string Describe(BenchArgs args) => string.Join(' ', BuildArgs(args));

    public List<string> BuildArgs(BenchArgs args)
    {
        var a = new List<string> { "--model", args.ModelPath };

        bool fit = args.UseFit && _capabilities.SupportsFit;

        if (args.BatchSize is { } b) { a.Add("--batch-size"); a.Add(b.ToString()); }
        if (args.UBatchSize is { } ub) { a.Add("--ubatch-size"); a.Add(ub.ToString()); }
        if (args.Threads is { } t) { a.Add("--threads"); a.Add(t.ToString()); }
        if (!fit && args.GpuLayers is { } ngl) { a.Add("-ngl"); a.Add(ngl.ToString()); }
        if (args.NCpuMoe is { } ncmoe && _capabilities.SupportsNCpuMoe) { a.Add("--n-cpu-moe"); a.Add(ncmoe.ToString()); }

        if (fit)
        {
            a.Add(_capabilities.FitTargetFlag); a.Add(args.FitMarginMiB.ToString());
            if (args.CtxSize is { } fc) { a.Add(_capabilities.FitCtxFlag); a.Add(fc.ToString()); }
        }
        else if (args.CtxSize is { } c && _capabilities.SupportsCtxSize) { a.Add(_capabilities.CtxFlag); a.Add(c.ToString()); }
        if (_capabilities.SupportsCacheType)
        {
            if (!string.IsNullOrWhiteSpace(args.CacheTypeK)) { a.Add("-ctk"); a.Add(args.CacheTypeK!); }
            if (!string.IsNullOrWhiteSpace(args.CacheTypeV)) { a.Add("-ctv"); a.Add(args.CacheTypeV!); }
        }
        if (_capabilities.SupportsMmproj && !string.IsNullOrWhiteSpace(args.MmprojPath))
        {
            a.Add("--mmproj"); a.Add(args.MmprojPath!);
        }

        a.Add("-r"); a.Add(args.Repeat.ToString());
        a.Add("-o"); a.Add("csv");

        if (args.NoWarmup && _capabilities.SupportsNoWarmup)
            a.Add("--no-warmup");

        a.Add("-n"); a.Add(args.NGen.ToString());
        a.Add("-p"); a.Add(args.NPrompt.ToString());

        bool quantizedKv = _capabilities.SupportsCacheType &&
                           (IsQuantizedCache(args.CacheTypeK) || IsQuantizedCache(args.CacheTypeV));
        bool? flashEff = args.FlashAttn;
        if (quantizedKv && flashEff != true)
            flashEff = true;
        if (flashEff is { } fa)
        {
            a.Add("--flash-attn");
            a.Add(_capabilities.FlashAttn == FlashAttnStyle.OnOff
                ? (fa ? "on" : "off")
                : (fa ? "1" : "0"));
        }

        if (!string.IsNullOrEmpty(args.OverridePattern) && _capabilities.SupportsOverrideTensor)
        {
            a.Add("--override-tensor");
            a.Add(args.OverridePattern!);
        }

        return a;
    }


    private static void KillTree(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch {}
    }

    private static bool IsQuantizedCache(string? type) =>
        !string.IsNullOrWhiteSpace(type)
        && !type.Equals("f16", StringComparison.OrdinalIgnoreCase)
        && !type.Equals("f32", StringComparison.OrdinalIgnoreCase);

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : "…" + s.Substring(s.Length - max);
}
