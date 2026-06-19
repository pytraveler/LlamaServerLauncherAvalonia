using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LlamaServerLauncher.Models.Optimization;

namespace LlamaServerLauncher.Services.Optimization;

public sealed class ServerBenchmarkService : IConfigBenchmarker
{
    private readonly string _serverExePath;
    private readonly HttpBenchmarkService _http;
    private readonly LogService? _log;
    private readonly string _host;
    private readonly bool _noMmap;
    private readonly int _healthTimeoutSeconds;

    public ServerBenchmarkService(
        string serverExePath,
        HttpBenchmarkService? http = null,
        LogService? log = null,
        string host = ServerArgvBuilder.DefaultHost,
        bool noMmap = false,
        int healthTimeoutSeconds = OptimizationConstants.ServerHealthTimeoutSeconds)
    {
        _serverExePath = serverExePath;
        _http = http ?? new HttpBenchmarkService(log: log);
        _log = log;
        _host = host;
        _noMmap = noMmap;
        _healthTimeoutSeconds = healthTimeoutSeconds;
    }

    public string ServerExePath => _serverExePath;

    public string Describe(BenchArgs args)
    {
        var argv = ServerArgvBuilder.Build(args, port: 0, host: _host);
        if (_noMmap) argv.Add("--no-mmap");
        return $"{_serverExePath} {string.Join(' ', argv)}";
    }

    public async Task<BenchmarkResult> RunBenchAsync(BenchArgs args, int timeoutSeconds, CancellationToken ct)
    {
        int port = ServerArgvBuilder.FindFreePort();
        var argv = ServerArgvBuilder.Build(args, port, _host);
        if (_noMmap) argv.Add("--no-mmap");
        string baseUrl = ServerArgvBuilder.BaseUrl(port, _host);

        var psi = new ProcessStartInfo
        {
            FileName = _serverExePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in argv)
            psi.ArgumentList.Add(a);

        var errTail = new RingBuffer(4000);

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) errTail.Append(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) errTail.Append(e.Data); };

        _log?.Debug($"server-bench: {_serverExePath} {string.Join(' ', argv)}");

        using var killReg = ct.Register(() => KillTree(process));

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            bool ready = false;
            var deadline = DateTime.UtcNow.AddSeconds(_healthTimeoutSeconds);
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                if (process.HasExited) break;
                if (await _http.CheckHealthOnceAsync(baseUrl, ct)) { ready = true; break; }
                await Task.Delay(500, ct);
            }
            if (!ready)
            {
                bool exited = process.HasExited;
                throw new InvalidOperationException(
                    exited
                        ? $"llama-server exited during startup (code {SafeExitCode(process)}). stderr tail: {errTail}"
                        : $"llama-server did not become healthy within {_healthTimeoutSeconds}s. stderr tail: {errTail}");
            }

            string prompt = BuildPrompt(args.NPrompt);
            int nPredict = args.NGen > 0 ? args.NGen : 1;

            int reps = Math.Max(1, args.Repeat);
            double tgSum = 0, ppSum = 0;
            int counted = 0;
            for (int i = 0; i < reps; i++)
            {
                ct.ThrowIfCancellationRequested();
                var r = await _http.MeasureAsync(baseUrl, prompt, nPredict, timeoutSeconds, ct);
                if (reps > 1 && i == 0)
                    continue;
                tgSum += r.TgTs;
                ppSum += r.PpTs;
                counted++;
            }
            if (counted == 0) counted = 1;
            return new BenchmarkResult { TgTs = tgSum / counted, PpTs = ppSum / counted };
        }
        finally
        {
            KillTree(process);
            try { await process.WaitForExitAsync(CancellationToken.None); } catch {}
        }
    }

    public static string BuildPrompt(int approxTokens)
    {
        int words = Math.Max(8, approxTokens);
        var sb = new StringBuilder(words * 5);
        for (int i = 0; i < words; i++)
            sb.Append(i == 0 ? "word" : " word");
        return sb.ToString();
    }

    private static void KillTree(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch {}
    }

    private static int SafeExitCode(Process p)
    {
        try { return p.ExitCode; } catch { return -1; }
    }

    private sealed class RingBuffer
    {
        private readonly int _max;
        private readonly StringBuilder _sb = new();
        public RingBuffer(int max) => _max = max;
        public void Append(string line)
        {
            _sb.Append(line).Append('\n');
            if (_sb.Length > _max)
                _sb.Remove(0, _sb.Length - _max);
        }
        public override string ToString() => _sb.ToString();
    }
}
