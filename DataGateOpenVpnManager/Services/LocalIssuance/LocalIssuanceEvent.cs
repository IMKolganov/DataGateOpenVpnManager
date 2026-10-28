namespace DataGateOpenVpnManager.Services.LocalIssuance;

public static class LocalIssuanceEventTypes
{
    public const string Issued = "issued";
    public const string Revoked = "revoked";
}

/// <summary>
/// Node → backend notice that a certificate was issued or revoked on the node itself.
/// Idempotency key on the backend: (server, <see cref="SerialNumber"/>, <see cref="EventType"/>).
/// </summary>
public sealed class LocalIssuanceEvent
{
    public Guid EventId { get; set; } = Guid.NewGuid();
    public string EventType { get; set; } = LocalIssuanceEventTypes.Issued;
    public string Origin { get; set; } = "local";
    public string CommonName { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string CertFingerprintSha256 { get; set; } = string.Empty;
    public bool IsServiceProfile { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string IssuedTo { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string CertFilePath { get; set; } = string.Empty;
    public string KeyFilePath { get; set; } = string.Empty;
}
