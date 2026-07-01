using DataGateOpenVpnManager.Services.Proxy;

namespace DataGateOpenVpnManager.Services.OpenVpnTls;

public sealed class OpenVpnTlsErrorClassifier(
    IActiveProxyConnectionService activeProxyConnections,
    IProxyTrafficFlowService trafficFlow) : IOpenVpnTlsErrorClassifier
{
    public bool IsTlsCryptLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var lower = line.ToLowerInvariant();
        return lower.Contains("tls error", StringComparison.Ordinal)
               && (lower.Contains("tls-crypt", StringComparison.Ordinal)
                   || lower.Contains("packet authentication failed", StringComparison.Ordinal))
               || lower.Contains("tls-crypt unwrap", StringComparison.Ordinal)
               || lower.Contains("tls-crypt unwrapp", StringComparison.Ordinal);
    }

    public OpenVpnTlsErrorContext Classify(string line)
    {
        OpenVpnTlsPeerParser.TryExtractPeer(line, out var peer, out var host, out var port);

        if (!OpenVpnTlsPeerParser.IsLoopbackHost(host))
        {
            return new OpenVpnTlsErrorContext
            {
                Origin = OpenVpnTlsErrorOrigin.ExternalProbe,
                RawLine = line,
                Peer = string.IsNullOrWhiteSpace(peer) ? null : peer,
                PeerHost = host,
                PeerPort = port
            };
        }

        var proxyConn = port > 0
            ? activeProxyConnections.TryGetByLocalProxy(port, host)
            : null;

        trafficFlow.TryGetIdentityByLocalProxy(port, host, out var identity);

        if (proxyConn is not null || identity is not null)
        {
            return new OpenVpnTlsErrorContext
            {
                Origin = OpenVpnTlsErrorOrigin.AppViaProxy,
                RawLine = line,
                Peer = peer,
                PeerHost = host,
                PeerPort = port,
                ConnectionId = proxyConn?.ConnectionId ?? identity?.ConnectionId,
                ClientRef = identity?.ClientRef,
                UserId = identity?.UserId,
                Username = identity?.Username,
                Email = identity?.Email,
                UserAgent = identity?.UserAgent,
                RealClientIp = proxyConn?.RealClientIp ?? identity?.RealClientIp,
                RealClientPort = proxyConn?.RealClientPort ?? identity?.RealClientPort ?? 0,
                LocalProxyPort = proxyConn?.LocalProxyPort ?? identity?.LocalProxyPort ?? port
            };
        }

        return new OpenVpnTlsErrorContext
        {
            Origin = OpenVpnTlsErrorOrigin.LocalUnknown,
            RawLine = line,
            Peer = peer,
            PeerHost = host,
            PeerPort = port,
            LocalProxyPort = port
        };
    }
}
