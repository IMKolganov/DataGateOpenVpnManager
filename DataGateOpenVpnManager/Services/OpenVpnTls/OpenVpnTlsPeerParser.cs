using System.Net;
using System.Text.RegularExpressions;

namespace DataGateOpenVpnManager.Services.OpenVpnTls;

internal static partial class OpenVpnTlsPeerParser
{
    [GeneratedRegex(@"\[AF_INET6?\]([^\s\]]+)", RegexOptions.CultureInvariant)]
    private static partial Regex AfInetPeerRegex();

    [GeneratedRegex(@"\bfrom\s+([0-9a-fA-F:\.]+:\d+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex FromPeerRegex();

    public static bool TryExtractPeer(string line, out string peer, out string? host, out int port)
    {
        peer = string.Empty;
        host = null;
        port = 0;

        if (string.IsNullOrWhiteSpace(line))
            return false;

        var match = AfInetPeerRegex().Match(line);
        if (!match.Success)
            match = FromPeerRegex().Match(line);

        if (!match.Success)
            return false;

        peer = match.Groups[1].Value.Trim();
        if (!TrySplitHostPort(peer, out host, out port))
            return false;

        return true;
    }

    public static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        var normalized = host.Trim();
        if (normalized.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!IPAddress.TryParse(normalized, out var ip))
            return false;

        return IPAddress.IsLoopback(ip);
    }

    private static bool TrySplitHostPort(string peer, out string? host, out int port)
    {
        host = null;
        port = 0;

        if (string.IsNullOrWhiteSpace(peer))
            return false;

        // IPv6 with brackets: [::1]:12345
        if (peer.StartsWith('['))
        {
            var close = peer.IndexOf(']');
            if (close <= 1 || close + 2 >= peer.Length || peer[close + 1] != ':')
                return false;

            host = peer[1..close];
            return int.TryParse(peer[(close + 2)..], out port) && port is > 0 and <= 65535;
        }

        var lastColon = peer.LastIndexOf(':');
        if (lastColon <= 0 || lastColon == peer.Length - 1)
            return false;

        host = peer[..lastColon];
        return int.TryParse(peer[(lastColon + 1)..], out port) && port is > 0 and <= 65535;
    }
}
