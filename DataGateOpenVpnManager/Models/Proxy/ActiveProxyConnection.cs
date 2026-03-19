using DataGateOpenVpnManager.Models.Proxy.Enums;

namespace DataGateOpenVpnManager.Models.Proxy;

public sealed class ActiveProxyConnection
{
    public required string ConnectionId { get; init; }
    public required ProxyConnectionProtocol Protocol { get; init; }

    public string? RealClientIp { get; init; }
    public int RealClientPort { get; init; }

    public string? LocalProxyIp { get; init; }
    public int LocalProxyPort { get; init; }

    public string? TargetIp { get; init; }
    public int TargetPort { get; init; }

    public required DateTime ConnectedAtUtc { get; init; }
}