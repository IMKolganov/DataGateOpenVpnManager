using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy.Enums;

namespace DataGateOpenVpnManager.Services.Proxy;

public sealed class ProxyTrafficFlowUpdate
{
    public required string ConnectionId { get; init; }
    public required ProxyConnectionProtocol Protocol { get; init; }
    public required string State { get; init; }
    public required bool IsConnected { get; init; }
    public required bool IsIdle { get; init; }
    public string? RealClientIp { get; init; }
    public int RealClientPort { get; init; }
    public string? ClientRef { get; init; }
    public string? UserId { get; init; }
    public string? Username { get; init; }
    public string? Email { get; init; }
    public string? LocalProxyIp { get; init; }
    public int LocalProxyPort { get; init; }
    public string? TargetIp { get; init; }
    public int TargetPort { get; init; }
    public long ClientToServerBytesTotal { get; init; }
    public long ServerToClientBytesTotal { get; init; }
    public long ClientToServerBytesDelta { get; init; }
    public long ServerToClientBytesDelta { get; init; }
    public DateTime ConnectedAtUtc { get; init; }
    public DateTime LastActivityAtUtc { get; init; }
    public DateTime EmittedAtUtc { get; init; }
    public string? ErrorMessage { get; init; }
}
