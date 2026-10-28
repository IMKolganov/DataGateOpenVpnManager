using System.Net;
using System.Net.Sockets;

namespace DataGateOpenVpnManager.Helpers;

/// <summary>IPv4 VPN pool from VPN_SUBNET / VPN_NETMASK (OpenVPN <c>server</c> + <c>topology subnet</c>).</summary>
public sealed class VpnSubnet
{
    private readonly uint _network;
    private readonly uint _mask;

    private VpnSubnet(uint network, uint mask)
    {
        _network = network & mask;
        _mask = mask;
    }

    public IPAddress Network => ToAddress(_network);
    public IPAddress Netmask => ToAddress(_mask);
    public IPAddress Broadcast => ToAddress(_network | ~_mask);

    /// <summary>OpenVPN takes the first host address of the pool for the server side of tun.</summary>
    public IPAddress ServerAddress => ToAddress(_network + 1);

    public static VpnSubnet Parse(string subnet, string netmask)
    {
        var network = ToUInt(ParseIPv4(subnet, "VPN_SUBNET"));
        var mask = ToUInt(ParseIPv4(netmask, "VPN_NETMASK"));
        // Contiguous mask: inverted mask + 1 must be a power of two.
        var inverted = ~mask;
        if (mask == 0 || (inverted & (inverted + 1)) != 0)
            throw new ArgumentException($"VPN_NETMASK '{netmask}' is not a contiguous netmask.");
        return new VpnSubnet(network, mask);
    }

    public bool Contains(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetwork && (ToUInt(address) & _mask) == _network;

    /// <summary>True for a host address a client may hold: inside the subnet, not network/broadcast/server.</summary>
    public bool IsAssignableClientAddress(IPAddress address, out string reason)
    {
        if (!Contains(address))
        {
            reason = $"{address} is outside VPN subnet {Network}/{Netmask}.";
            return false;
        }

        var value = ToUInt(address);
        if (value == _network)
        {
            reason = $"{address} is the network address of the VPN subnet.";
            return false;
        }

        if (value == (_network | ~_mask))
        {
            reason = $"{address} is the broadcast address of the VPN subnet.";
            return false;
        }

        if (value == _network + 1)
        {
            reason = $"{address} is the OpenVPN server address.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public static IPAddress ParseIPv4(string value, string name)
    {
        if (!IPAddress.TryParse(value?.Trim(), out var address)
            || address.AddressFamily != AddressFamily.InterNetwork
            || value!.Trim().Count(c => c == '.') != 3)
            throw new ArgumentException($"{name} '{value}' is not a valid IPv4 address.");
        return address;
    }

    private static uint ToUInt(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static IPAddress ToAddress(uint value) =>
        new([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
}
