using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace LlamaServerLauncher.Models;

public enum ServerBufferKind
{
    Weights,
    Cache,
    Compute
}

public enum ServerMemorySource
{
    None,
    Buffers,
    Breakdown
}

public readonly record struct ServerBuffer(string Device, ServerBufferKind Kind, long Bytes, bool OnHost);

public readonly record struct ServerMemoryRow(
    string Device,
    bool OnHost,
    long WeightBytes,
    long CacheBytes,
    long ComputeBytes,
    long UnaccountedBytes,
    long DeviceTotalBytes,
    long DeviceFreeBytes);

public sealed record ServerMemoryReport
{
    public long WeightBytes { get; init; }
    public long CacheBytes { get; init; }
    public long ComputeBytes { get; init; }
    public long UnaccountedBytes { get; init; }
    public long HostWeightBytes { get; init; }
    public long HostCacheBytes { get; init; }
    public long HostComputeBytes { get; init; }
    public int DeviceCount { get; init; }
    public int OffloadedLayers { get; init; }
    public int TotalLayers { get; init; }
    public ServerMemorySource Source { get; init; }

    public long TotalBytes => WeightBytes + CacheBytes + ComputeBytes + UnaccountedBytes;
    public long HostBytes => HostWeightBytes + HostCacheBytes + HostComputeBytes;
    public bool HasAny => Source != ServerMemorySource.None && TotalBytes > 0;
    public bool HasLayers => TotalLayers > 0;
}

public static class ServerMemoryParser
{
    public const string BreakdownHeader = "memory breakdown";

    private const string BreakdownTag = "breakdown";
    private const long Mib = 1024 * 1024;

    private static readonly Regex BufferRegex = new(
        @"(?<device>[A-Za-z0-9_.:+\-]+)\s+(?<kind>model|KV|RS|LoRA|output|compute)\s+buffer\s+size\s*=\s*(?<value>[0-9]+(?:\.[0-9]+)?)\s*(?<unit>[KMG]i?B)",
        RegexOptions.Compiled);

    private static readonly Regex DeviceRowRegex = new(
        @"-\s+(?<device>\S+)[^|]*\|\s*(?<total>\d+)\s*=\s*(?<free>\d+)\s*\+\s*\(\s*\d+\s*=\s*(?<model>\d+)\s*\+\s*(?<cache>\d+)\s*\+\s*(?<compute>\d+)\s*\)\s*\+\s*(?<unaccounted>-?\d+)",
        RegexOptions.Compiled);

    private static readonly Regex LayersRegex = new(
        @"offloaded\s+(?<gpu>\d+)\s*/\s*(?<all>\d+)\s+layers\s+to\s+GPU",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex HostRowRegex = new(
        @"-\s+(?<device>\S+)[^|]*\|\s*\d+\s*=\s*(?<model>\d+)\s*\+\s*(?<cache>\d+)\s*\+\s*(?<compute>\d+)",
        RegexOptions.Compiled);

    public static ServerBuffer? TryParseBuffer(string? line)
    {
        if (string.IsNullOrEmpty(line)) return null;
        if (line.IndexOf("buffer size", StringComparison.Ordinal) < 0) return null;

        var match = BufferRegex.Match(line);
        if (!match.Success) return null;

        if (!double.TryParse(match.Groups["value"].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double value) || value < 0)
            return null;

        var device = match.Groups["device"].Value;
        long bytes = (long)Math.Round(value * UnitScale(match.Groups["unit"].Value));
        return new ServerBuffer(device, KindOf(match.Groups["kind"].Value), bytes, IsHostDevice(device));
    }

    public static ServerMemoryRow? TryParseBreakdownRow(string? line)
    {
        if (string.IsNullOrEmpty(line) || line.IndexOf('|') < 0) return null;
        if (line.IndexOf(BreakdownTag, StringComparison.OrdinalIgnoreCase) < 0) return null;
        if (line.IndexOf(BreakdownHeader, StringComparison.Ordinal) >= 0) return null;

        var device = DeviceRowRegex.Match(line);
        if (device.Success)
            return new ServerMemoryRow(
                device.Groups["device"].Value,
                IsHostDevice(device.Groups["device"].Value),
                Mib * Number(device, "model"),
                Mib * Number(device, "cache"),
                Mib * Number(device, "compute"),
                Mib * NonNegative(device, "unaccounted"),
                Mib * Number(device, "total"),
                Mib * Number(device, "free"));

        var host = HostRowRegex.Match(line);
        if (!host.Success) return null;

        return new ServerMemoryRow(
            host.Groups["device"].Value,
            IsHostDevice(host.Groups["device"].Value),
            Mib * Number(host, "model"),
            Mib * Number(host, "cache"),
            Mib * Number(host, "compute"),
            0, 0, 0);
    }

    public static (int Offloaded, int Total)? TryParseLayers(string? line)
    {
        if (string.IsNullOrEmpty(line)) return null;
        if (line.IndexOf("layers to GPU", StringComparison.OrdinalIgnoreCase) < 0) return null;

        var match = LayersRegex.Match(line);
        if (!match.Success) return null;

        if (!int.TryParse(match.Groups["gpu"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int gpu)
            || !int.TryParse(match.Groups["all"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int all)
            || all <= 0 || gpu > all)
            return null;

        return (gpu, all);
    }

    public static bool IsBreakdownHeader(string? line) =>
        !string.IsNullOrEmpty(line) && line.IndexOf(BreakdownHeader, StringComparison.Ordinal) >= 0;

    public static bool IsHostDevice(string? device) =>
        !string.IsNullOrEmpty(device)
        && (device.Equals("CPU", StringComparison.OrdinalIgnoreCase)
            || device.Equals("Host", StringComparison.OrdinalIgnoreCase)
            || device.Equals("BLAS", StringComparison.OrdinalIgnoreCase)
            || device.StartsWith("CPU_", StringComparison.OrdinalIgnoreCase)
            || device.EndsWith("_Host", StringComparison.OrdinalIgnoreCase));

    private static long Number(Match match, string group) =>
        long.TryParse(match.Groups[group].Value, NumberStyles.None,
            CultureInfo.InvariantCulture, out long value) ? value : 0;

    private static long NonNegative(Match match, string group) =>
        long.TryParse(match.Groups[group].Value, NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out long value) ? Math.Max(0, value) : 0;

    private static ServerBufferKind KindOf(string kind) => kind switch
    {
        "KV" or "RS" => ServerBufferKind.Cache,
        "compute" or "output" => ServerBufferKind.Compute,
        _ => ServerBufferKind.Weights
    };

    private static double UnitScale(string unit) => unit.ToUpperInvariant() switch
    {
        "GIB" or "GB" => 1024.0 * 1024 * 1024,
        "KIB" or "KB" => 1024.0,
        _ => 1024.0 * 1024
    };
}

public sealed class ServerMemoryAccumulator
{
    private readonly object _lock = new();
    private readonly HashSet<string> _bufferDevices = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ServerMemoryRow> _rows = new();
    private long _weights, _cache, _compute;
    private long _hostWeights, _hostCache, _hostCompute;
    private int _offloadedLayers, _totalLayers;

    public bool Add(string? line)
    {
        if (ServerMemoryParser.IsBreakdownHeader(line))
        {
            lock (_lock) _rows.Clear();
            return true;
        }

        if (ServerMemoryParser.TryParseBreakdownRow(line) is { } row)
        {
            lock (_lock) _rows.Add(row);
            return true;
        }

        if (ServerMemoryParser.TryParseLayers(line) is { } layers)
        {
            lock (_lock)
            {
                ClearMeasurements();
                _offloadedLayers = layers.Offloaded;
                _totalLayers = layers.Total;
            }
            return true;
        }

        if (ServerMemoryParser.TryParseBuffer(line) is not { } buffer) return false;

        lock (_lock)
        {
            if (!buffer.OnHost) _bufferDevices.Add(buffer.Device);

            switch (buffer.Kind)
            {
                case ServerBufferKind.Weights:
                    if (buffer.OnHost) _hostWeights += buffer.Bytes; else _weights += buffer.Bytes;
                    break;
                case ServerBufferKind.Cache:
                    if (buffer.OnHost) _hostCache += buffer.Bytes; else _cache += buffer.Bytes;
                    break;
                default:
                    if (buffer.OnHost) _hostCompute += buffer.Bytes; else _compute += buffer.Bytes;
                    break;
            }
        }

        return true;
    }

    public ServerMemoryReport Snapshot()
    {
        lock (_lock)
        {
            return _rows.Count > 0 ? FromRows() : FromBuffers();
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            ClearMeasurements();
            _offloadedLayers = _totalLayers = 0;
        }
    }

    private void ClearMeasurements()
    {
        _rows.Clear();
        _bufferDevices.Clear();
        _weights = _cache = _compute = 0;
        _hostWeights = _hostCache = _hostCompute = 0;
    }

    private ServerMemoryReport FromRows()
    {
        int devices = 0;
        long weights = 0, cache = 0, compute = 0, unaccounted = 0;
        long hostWeights = 0, hostCache = 0, hostCompute = 0;

        foreach (var row in _rows)
        {
            if (row.OnHost)
            {
                hostWeights += row.WeightBytes;
                hostCache += row.CacheBytes;
                hostCompute += row.ComputeBytes;
                continue;
            }

            devices++;
            weights += row.WeightBytes;
            cache += row.CacheBytes;
            compute += row.ComputeBytes;
            unaccounted += row.UnaccountedBytes;
        }

        return new ServerMemoryReport
        {
            WeightBytes = weights,
            CacheBytes = cache,
            ComputeBytes = compute,
            UnaccountedBytes = unaccounted,
            HostWeightBytes = hostWeights,
            HostCacheBytes = hostCache,
            HostComputeBytes = hostCompute,
            DeviceCount = devices,
            OffloadedLayers = _offloadedLayers,
            TotalLayers = _totalLayers,
            Source = ServerMemorySource.Breakdown,
        };
    }

    private ServerMemoryReport FromBuffers()
    {
        bool any = _weights > 0 || _cache > 0 || _compute > 0
            || _hostWeights > 0 || _hostCache > 0 || _hostCompute > 0;

        return new ServerMemoryReport
        {
            WeightBytes = _weights,
            CacheBytes = _cache,
            ComputeBytes = _compute,
            HostWeightBytes = _hostWeights,
            HostCacheBytes = _hostCache,
            HostComputeBytes = _hostCompute,
            DeviceCount = _bufferDevices.Count,
            OffloadedLayers = _offloadedLayers,
            TotalLayers = _totalLayers,
            Source = any ? ServerMemorySource.Buffers : ServerMemorySource.None,
        };
    }
}
