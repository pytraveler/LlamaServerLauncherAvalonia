using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using LlamaServerLauncher.Models;

namespace LlamaServerLauncher.Services;

public static class LocalAddressService
{
    public static List<LocalAddressEntry> Enumerate()
    {
        var candidates = new List<LocalAddressCandidate>();

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                IPInterfaceProperties props;
                try { props = ni.GetIPProperties(); } catch { continue; }

                var hasGateway = props.GatewayAddresses.Any(g =>
                    g?.Address != null &&
                    g.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !g.Address.Equals(IPAddress.Any));

                foreach (var unicast in props.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    candidates.Add(new LocalAddressCandidate
                    {
                        Address = unicast.Address.ToString(),
                        InterfaceName = ni.Name,
                        IsUp = true,
                        HasGateway = hasGateway
                    });
                }
            }
        }
        catch
        {
            // No interfaces readable: the loopback entry below still gives the user something usable.
        }

        string? hostName = null;
        try { hostName = Dns.GetHostName(); } catch { }

        return LocalAddressPicker.Order(candidates, hostName);
    }
}
