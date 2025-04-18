namespace DataGateCertManager.Models;

public class CertificateBuildResult
{
    public string CertificatePath { get; set; } = string.Empty;
    public string KeyPath { get; set; } = string.Empty;
    public string RequestPath { get; set; } = string.Empty;
    public string PemPath { get; set; } = string.Empty;
    public string? CertId { get; set; }
}