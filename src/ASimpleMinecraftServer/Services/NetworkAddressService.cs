using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ASimpleMinecraftServer.Services;

/// <summary>
/// Finds the addresses players use to connect: this PC's LAN IPv4 address (worked out locally)
/// and its public internet address (looked up online, only when the user asks for it).
/// </summary>
public sealed class NetworkAddressService(DownloadService downloads)
{
    // Returns the caller's public IP as plain text. Used only when the user clicks "Show".
    private const string PublicIpLookupUrl = "https://api.ipify.org";

    /// <summary>
    /// IPv4 addresses of active, physical-looking adapters that have a default gateway
    /// (the ones other devices on the home network can reach), best first.
    /// </summary>
    public static IReadOnlyList<string> GetLanAddresses()
    {
        var results = new List<(string Address, bool HasGateway)>();
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                if (adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var description = adapter.Description + " " + adapter.Name;
                if (ClassifyOverlay(description, string.Empty) is not null) continue;
                if (description.Contains("virtual", StringComparison.OrdinalIgnoreCase)
                    || description.Contains("vEthernet", StringComparison.OrdinalIgnoreCase)
                    || description.Contains("VPN", StringComparison.OrdinalIgnoreCase)) continue;

                var properties = adapter.GetIPProperties();
                var hasGateway = properties.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                foreach (var unicast in properties.UnicastAddresses)
                {
                    var address = unicast.Address;
                    if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address)) continue;
                    if (address.ToString().StartsWith("169.254.", StringComparison.Ordinal)) continue; // no DHCP lease
                    results.Add((address.ToString(), hasGateway));
                }
            }
        }
        catch (NetworkInformationException) { }

        return results.OrderByDescending(r => r.HasGateway).Select(r => r.Address).Distinct().ToList();
    }

    /// <summary>
    /// Addresses on private overlay networks (Tailscale, ZeroTier, Hamachi, Radmin VPN). Friends who
    /// join the same network can connect to these without any router port forwarding.
    /// </summary>
    public static IReadOnlyList<(string Network, string Address)> GetOverlayAddresses()
    {
        var results = new List<(string Network, string Address)>();
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var address = unicast.Address.ToString();
                    if (ClassifyOverlay(adapter.Description + " " + adapter.Name, address) is { } network)
                        results.Add((network, address));
                }
            }
        }
        catch (NetworkInformationException) { }
        return results.Distinct().ToList();
    }

    /// <summary>
    /// Names the overlay network an adapter belongs to, from its description or, for Tailscale,
    /// its address range (100.64.0.0/10). Null for ordinary adapters.
    /// </summary>
    public static string? ClassifyOverlay(string adapterDescription, string ipv4Address)
    {
        if (adapterDescription.Contains("Tailscale", StringComparison.OrdinalIgnoreCase)) return "Tailscale";
        if (adapterDescription.Contains("ZeroTier", StringComparison.OrdinalIgnoreCase)) return "ZeroTier";
        if (adapterDescription.Contains("Hamachi", StringComparison.OrdinalIgnoreCase)) return "Hamachi";
        if (adapterDescription.Contains("Radmin", StringComparison.OrdinalIgnoreCase)) return "Radmin VPN";
        if (IPAddress.TryParse(ipv4Address, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) return "Tailscale"; // CGNAT range Tailscale uses
        }
        return null;
    }

    /// <summary>Looks up this network's public IPv4 address. Throws if the answer isn't a valid IP.</summary>
    public async Task<string> GetPublicAddressAsync(CancellationToken cancellationToken = default)
    {
        var text = (await downloads.GetStringAsync(PublicIpLookupUrl, cancellationToken)).Trim();
        if (!IPAddress.TryParse(text, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
            throw new InvalidDataException("The public IP lookup returned an unexpected answer.");
        return address.ToString();
    }
}
