using System.Collections.Generic;
using System.Linq;
using LlamaServerLauncher.Models;

public static class LocalAddressTests
{
    private static LocalAddressCandidate Candidate(string address, string name, bool up = true, bool gateway = false) =>
        new() { Address = address, InterfaceName = name, IsUp = up, HasGateway = gateway };

    public static void Run(Harness h)
    {
        h.Section("LocalAddressPicker - what gets dropped");
        h.Check("link-local recognized", LocalAddressPicker.IsLinkLocal("169.254.73.220"), "169.254.73.220");
        h.Check("ordinary address is not link-local", !LocalAddressPicker.IsLinkLocal("192.168.3.36"), "192.168.3.36");
        h.Check("loopback recognized", LocalAddressPicker.IsLoopback("127.0.0.1"), "127.0.0.1");

        var noisy = new List<LocalAddressCandidate>
        {
            Candidate("169.254.73.220", "Ethernet 2", up: false),
            Candidate("169.254.104.191", "outline-tap0"),
            Candidate("127.0.0.1", "Loopback")
        };
        var cleaned = LocalAddressPicker.Order(noisy, "LeePC");
        h.Check("disconnected adapter dropped", cleaned.All(e => e.Address != "169.254.73.220"), string.Join(",", cleaned.Select(e => e.Address)));
        h.Check("link-local dropped even when up", cleaned.All(e => e.Address != "169.254.104.191"), string.Join(",", cleaned.Select(e => e.Address)));
        h.Check("loopback listed once", cleaned.Count(e => e.Address == "127.0.0.1") == 1, cleaned.Count(e => e.Address == "127.0.0.1").ToString());

        h.Section("LocalAddressPicker - order");
        var real = new List<LocalAddressCandidate>
        {
            Candidate("192.168.162.1", "VMware Network Adapter VMnet8"),
            Candidate("172.29.128.1", "vEthernet (Default Switch)"),
            Candidate("10.67.216.205", "redlinklx7okpsv"),
            Candidate("192.168.3.36", "Беспроводная сеть", gateway: true),
            Candidate("192.168.101.1", "VMware Network Adapter VMnet1")
        };
        var ordered = LocalAddressPicker.Order(real, "LeePC");
        h.Check("lan address first", ordered[0].Address == "192.168.3.36", ordered[0].Address);
        h.Check("lan kind", ordered[0].Kind == LocalAddressKind.Lan, ordered[0].Kind.ToString());
        h.Check("interface name kept", ordered[0].InterfaceName == "Беспроводная сеть", ordered[0].InterfaceName);
        h.Check("loopback second", ordered[1].Address == "127.0.0.1" && ordered[1].Kind == LocalAddressKind.Loopback, ordered[1].Address);
        h.Check("host name third", ordered[2].Address == "LeePC" && ordered[2].Kind == LocalAddressKind.HostName, ordered[2].Address);
        h.Check("gatewayless adapters last", ordered.Skip(3).All(e => e.Kind == LocalAddressKind.Other), string.Join(",", ordered.Skip(3).Select(e => e.Address)));
        h.Check("nothing lost", ordered.Count == 7, ordered.Count.ToString());
        h.Check("tunnel kept, order preserved", ordered[3].Address == "192.168.162.1" && ordered[5].Address == "10.67.216.205",
            string.Join(",", ordered.Skip(3).Select(e => e.Address)));

        h.Section("LocalAddressPicker - degenerate input");
        var empty = LocalAddressPicker.Order(null, null);
        h.Check("loopback always offered", empty.Count == 1 && empty[0].Address == "127.0.0.1", empty.Count.ToString());
        var hostOnly = LocalAddressPicker.Order(new List<LocalAddressCandidate>(), "  LeePC  ");
        h.Check("host name trimmed", hostOnly.Any(e => e.Address == "LeePC"), string.Join(",", hostOnly.Select(e => e.Address)));

        var duplicated = LocalAddressPicker.Order(new List<LocalAddressCandidate>
        {
            Candidate("192.168.3.36", "Wi-Fi", gateway: true),
            Candidate("192.168.3.36", "Wi-Fi (second binding)", gateway: true)
        }, "");
        h.Check("duplicate address kept once", duplicated.Count(e => e.Address == "192.168.3.36") == 1, duplicated.Count.ToString());
        h.Check("empty host name not offered", duplicated.All(e => e.Kind != LocalAddressKind.HostName), string.Join(",", duplicated.Select(e => e.Kind.ToString())));

        var hostSameAsAddress = LocalAddressPicker.Order(new List<LocalAddressCandidate>
        {
            Candidate("192.168.3.36", "Wi-Fi", gateway: true)
        }, "192.168.3.36");
        h.Check("host name equal to an address not duplicated", hostSameAsAddress.Count == 2,
            string.Join(",", hostSameAsAddress.Select(e => e.Address)));
    }
}
