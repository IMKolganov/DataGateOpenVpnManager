using System.Globalization;
using System.Net;

namespace DataGateOpenVpnManager.Services.Proxy;

public sealed record OpenVpnManagementClientEntry(
    string CommonName,
    string RealAddress,
    string VirtualAddress,
    long BytesReceived,
    long BytesSent,
    long ConnectedSinceUnix);

public static class OpenVpnManagementStatusParser
{
    public static IReadOnlyList<OpenVpnManagementClientEntry> ParseClientList(string statusResponse)
    {
        if (string.IsNullOrWhiteSpace(statusResponse))
            return Array.Empty<OpenVpnManagementClientEntry>();

        var result = new List<OpenVpnManagementClientEntry>();
        var lines = statusResponse.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            if (!line.StartsWith("CLIENT_LIST", StringComparison.Ordinal))
                continue;

            var parts = line.Split('\t');
            if (parts.Length < 8)
                continue;

            if (!long.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytesReceived))
                continue;
            if (!long.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytesSent))
                continue;
            if (!long.TryParse(parts[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out var connectedSince))
                connectedSince = 0;

            result.Add(new OpenVpnManagementClientEntry(
                parts[1],
                parts[2],
                parts[3],
                bytesReceived,
                bytesSent,
                connectedSince));
        }

        return result;
    }

    public static OpenVpnManagementClientEntry? FindByLocalProxyPort(
        IEnumerable<OpenVpnManagementClientEntry> clients,
        string? localProxyIp,
        int localProxyPort)
    {
        foreach (var client in clients)
        {
            if (!TryParseRealAddressPort(client.RealAddress, out var ip, out var port))
                continue;
            if (port != localProxyPort)
                continue;
            if (!LoopbackHostsEqual(localProxyIp, ip))
                continue;

            return client;
        }

        return null;
    }

    public static OpenVpnManagementClientEntry? FindByVirtualAddress(
        IEnumerable<OpenVpnManagementClientEntry> clients,
        string? virtualAddress)
    {
        if (string.IsNullOrWhiteSpace(virtualAddress))
            return null;

        foreach (var client in clients)
        {
            if (string.Equals(client.VirtualAddress, virtualAddress, StringComparison.OrdinalIgnoreCase))
                return client;
        }

        return null;
    }

    internal static bool LoopbackHostsEqual(string? expected, string actual)
    {
        var normalizedExpected = NormalizeLoopbackHost(expected);
        var normalizedActual = NormalizeLoopbackHost(actual);
        return string.Equals(normalizedExpected, normalizedActual, StringComparison.OrdinalIgnoreCase);
    }

    internal static string NormalizeLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return "127.0.0.1";

        var trimmed = host.Trim();
        if (trimmed.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return "127.0.0.1";

        if (!IPAddress.TryParse(trimmed, out var ip))
            return trimmed;

        if (IPAddress.IsLoopback(ip))
            return "127.0.0.1";

        return ip.ToString();
    }

    private static bool TryParseRealAddressPort(string realAddress, out string ip, out int port)
    {
        ip = string.Empty;
        port = 0;

        if (string.IsNullOrWhiteSpace(realAddress))
            return false;

        var lastColon = realAddress.LastIndexOf(':');
        if (lastColon <= 0 || lastColon >= realAddress.Length - 1)
            return false;

        ip = realAddress[..lastColon];
        if (ip.StartsWith('[') && ip.EndsWith(']'))
            ip = ip[1..^1];

        return int.TryParse(realAddress[(lastColon + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out port)
               && port is > 0 and <= 65535;
    }
}
