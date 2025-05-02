namespace DataGateCertManager.Models;

public class AddServerCertificateRequest
{
    public string CommonName { get; set; } = string.Empty;
    public int CertExpireDays { get; set; } = 365;
}