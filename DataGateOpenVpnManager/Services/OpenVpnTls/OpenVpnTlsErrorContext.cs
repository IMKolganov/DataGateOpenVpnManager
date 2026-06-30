namespace DataGateOpenVpnManager.Services.OpenVpnTls;

public sealed class OpenVpnTlsErrorContext
{
    public required OpenVpnTlsErrorOrigin Origin { get; init; }
    public required string RawLine { get; init; }
    public string? Peer { get; init; }
    public string? PeerHost { get; init; }
    public int PeerPort { get; init; }
    public string? ConnectionId { get; init; }
    public string? ClientRef { get; init; }
    public string? UserId { get; init; }
    public string? Username { get; init; }
    public string? Email { get; init; }
    public string? UserAgent { get; init; }
    public string? RealClientIp { get; init; }
    public int RealClientPort { get; init; }
    public int LocalProxyPort { get; init; }
}
