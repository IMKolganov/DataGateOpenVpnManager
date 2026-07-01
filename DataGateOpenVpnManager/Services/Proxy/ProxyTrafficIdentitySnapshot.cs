namespace DataGateOpenVpnManager.Services.Proxy;

public sealed class ProxyTrafficIdentitySnapshot
{
    public string? ConnectionId { get; init; }
    public string? ClientRef { get; init; }
    public string? UserId { get; init; }
    public string? Username { get; init; }
    public string? Email { get; init; }
    public string? UserAgent { get; init; }
    public string? RealClientIp { get; init; }
    public int RealClientPort { get; init; }
    public string? LocalProxyIp { get; init; }
    public int LocalProxyPort { get; init; }
}
