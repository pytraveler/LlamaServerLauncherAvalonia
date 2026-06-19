using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using LlamaServerLauncher.Models.Optimization;

namespace LlamaServerLauncher.Services.Optimization;

public static class ServerArgvBuilder
{
    public const string DefaultHost = "127.0.0.1";

    public static List<string> Build(BenchArgs args, int port, string host = DefaultHost)
    {
        var a = new List<string>
        {
            "--model", args.ModelPath,
            "--host", host,
            "--port", port.ToString(),
        };

        if (args.CtxSize is { } c) { a.Add("-c"); a.Add(c.ToString()); }
        if (args.Threads is { } t) { a.Add("-t"); a.Add(t.ToString()); }
        if (args.BatchSize is { } b) { a.Add("-b"); a.Add(b.ToString()); }
        if (args.UBatchSize is { } ub) { a.Add("-ub"); a.Add(ub.ToString()); }
        if (!args.UseFit && args.GpuLayers is { } ngl) { a.Add("-ngl"); a.Add(ngl.ToString()); }
        if (args.NCpuMoe is { } ncmoe) { a.Add("--n-cpu-moe"); a.Add(ncmoe.ToString()); }

        if (!string.IsNullOrWhiteSpace(args.CacheTypeK)) { a.Add("-ctk"); a.Add(args.CacheTypeK!); }
        if (!string.IsNullOrWhiteSpace(args.CacheTypeV)) { a.Add("-ctv"); a.Add(args.CacheTypeV!); }
        if (!string.IsNullOrWhiteSpace(args.MmprojPath)) { a.Add("--mmproj"); a.Add(args.MmprojPath!); }

        bool quantizedKv = IsQuantizedCache(args.CacheTypeK) || IsQuantizedCache(args.CacheTypeV);
        bool? flashEff = args.FlashAttn;
        if (quantizedKv && flashEff != true) flashEff = true;
        if (flashEff is { } fa) { a.Add("--flash-attn"); a.Add(fa ? "on" : "off"); }

        if (!string.IsNullOrEmpty(args.OverridePattern)) { a.Add("--override-tensor"); a.Add(args.OverridePattern!); }

        return a;
    }

    public static string BaseUrl(int port, string host = DefaultHost) => $"http://{host}:{port}";

    public static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    private static bool IsQuantizedCache(string? type) =>
        !string.IsNullOrWhiteSpace(type)
        && !type.Equals("f16", StringComparison.OrdinalIgnoreCase)
        && !type.Equals("f32", StringComparison.OrdinalIgnoreCase);
}
