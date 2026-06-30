namespace DataGateOpenVpnManager.Services.Interfaces;

/// <summary>HTTP caller metadata for JWT validation failure logs.</summary>
public sealed record JwtValidationRequestContext(
    string? RemoteIp,
    string Path,
    string Method,
    string? UserAgent = null);
