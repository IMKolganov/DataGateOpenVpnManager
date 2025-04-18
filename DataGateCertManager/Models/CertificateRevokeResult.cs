namespace DataGateCertManager.Models;

public class CertificateRevokeResult
{
    public string CertificatePath { get; set; } = string.Empty;
    //todo: think about it how get this params
    // public string KeyPath { get; set; } = string.Empty;
    // public string RequestPath { get; set; } = string.Empty;
    // public string PemPath { get; set; } = string.Empty;
    // public string? CertId { get; set; }
    public bool IsRevoked { get; set; }
    public string Message { get; set; } = string.Empty;
}