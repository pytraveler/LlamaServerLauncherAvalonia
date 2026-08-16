using System;
using System.Collections.Generic;
using System.Linq;

namespace LlamaServerLauncher.Models;

public enum LocalAddressKind
{
    Lan = 0,
    Loopback = 1,
    HostName = 2,
    Other = 3
}

public sealed class LocalAddressCandidate
{
    public string Address { get; init; } = "";
    public string InterfaceName { get; init; } = "";
    public bool IsUp { get; init; }
    public bool HasGateway { get; init; }
}

public sealed class LocalAddressEntry
{
    public string Address { get; init; } = "";
    public string InterfaceName { get; init; } = "";
    public LocalAddressKind Kind { get; init; }
}

public static class LocalAddressPicker
{
    public const string Loopback = "127.0.0.1";

    public static bool IsLinkLocal(string? address) =>
        (address ?? "").StartsWith("169.254.", StringComparison.Ordinal);

    public static bool IsLoopback(string? address) =>
        (address ?? "").StartsWith("127.", StringComparison.Ordinal);

    public static bool IsUsable(LocalAddressCandidate? c) =>
        c != null && c.IsUp && !string.IsNullOrWhiteSpace(c.Address)
        && !IsLinkLocal(c.Address) && !IsLoopback(c.Address);

    public static List<LocalAddressEntry> Order(IEnumerable<LocalAddressCandidate>? candidates, string? hostName)
    {
        var entries = new List<LocalAddressEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in (candidates ?? Enumerable.Empty<LocalAddressCandidate>()).Where(IsUsable))
        {
            var address = c!.Address.Trim();
            if (!seen.Add(address)) continue;
            entries.Add(new LocalAddressEntry
            {
                Address = address,
                InterfaceName = c.InterfaceName ?? "",
                Kind = c.HasGateway ? LocalAddressKind.Lan : LocalAddressKind.Other
            });
        }

        if (seen.Add(Loopback))
            entries.Add(new LocalAddressEntry { Address = Loopback, Kind = LocalAddressKind.Loopback });

        var host = (hostName ?? "").Trim();
        if (host.Length > 0 && seen.Add(host))
            entries.Add(new LocalAddressEntry { Address = host, Kind = LocalAddressKind.HostName });

        return entries.OrderBy(e => (int)e.Kind).ToList();
    }
}
