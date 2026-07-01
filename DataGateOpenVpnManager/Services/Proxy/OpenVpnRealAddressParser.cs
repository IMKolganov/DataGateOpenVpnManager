using System.Globalization;
using System.Net;

namespace DataGateOpenVpnManager.Services.Proxy;

/// <summary>
/// Parses OpenVPN management interface <c>RealAddress</c> (status 3).
/// </summary>
public static class OpenVpnRealAddressParser
{
    /// <summary>
    /// Legacy: <c>127.0.0.1:53188</c>, <c>[::1]:50000</c>.
    /// OpenVPN 2.7+: <c>tcp4-server:127.0.0.1:53188</c>, <c>udp4-server:...</c>.
    /// </summary>
    public static bool TryParseHostPort(string? realAddress, out string host, out int port)
    {
        host = string.Empty;
        port = 0;

        if (string.IsNullOrWhiteSpace(realAddress))
            return false;

        var trimmed = realAddress.Trim();
        if (TryParseAsEndPoint(trimmed, out host, out port))
            return true;

        var firstColon = trimmed.IndexOf(':');
        if (firstColon <= 0 || firstColon >= trimmed.Length - 1)
            return false;

        return TryParseAsEndPoint(trimmed[(firstColon + 1)..], out host, out port);
    }

    /// <summary>Canonical loopback <c>127.0.0.1:port</c> for stable session matching.</summary>
    public static string? CanonicalizeLoopback(string? realAddress)
    {
        if (!TryParseHostPort(realAddress, out var host, out var port))
            return null;

        if (!IsLoopbackHost(host))
            return null;

        return $"127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>Legacy <c>host:port</c> without OpenVPN 2.7 socket prefix.</summary>
    public static string? NormalizeEndpoint(string? realAddress)
    {
        if (!TryParseHostPort(realAddress, out var host, out var port))
            return null;

        return FormatHostPort(host, port, forceLoopbackHost: false);
    }

    internal static bool IsLoopbackHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        return IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip);
    }

    private static bool TryParseAsEndPoint(string value, out string host, out int port)
    {
        host = string.Empty;
        port = 0;

        if (IPEndPoint.TryParse(value, out var ep))
        {
            if (ep.Port is < 1 or > 65535)
                return false;

            host = ep.Address.ToString();
            port = ep.Port;
            return true;
        }

        // localhost:port — IPEndPoint.TryParse does not accept hostnames.
        var lastColon = value.LastIndexOf(':');
        if (lastColon <= 0 || lastColon >= value.Length - 1)
            return false;

        var hostPart = value[..lastColon];
        if (hostPart.StartsWith('[') && hostPart.EndsWith(']'))
            hostPart = hostPart[1..^1];
        else if (hostPart.Contains(':'))
            return false;

        if (!int.TryParse(value[(lastColon + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out port))
            return false;

        if (port is < 1 or > 65535)
            return false;

        host = hostPart;
        return true;
    }

    private static string FormatHostPort(string host, int port, bool forceLoopbackHost)
    {
        if (forceLoopbackHost || host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return $"127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}";

        if (IPAddress.TryParse(host, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            return $"[{host}]:{port.ToString(CultureInfo.InvariantCulture)}";

        return $"{host}:{port.ToString(CultureInfo.InvariantCulture)}";
    }
}
