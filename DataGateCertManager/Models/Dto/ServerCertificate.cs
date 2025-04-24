using DataGateCertManager.Models.Enums;

namespace DataGateCertManager.Models.Dto;

public class ServerCertificate
{
    public string CommonName { get; set; } = string.Empty;
    public CertificateStatus Status { get; set; } = CertificateStatus.Unknown;
    public string SerialNumber { get; set; } = string.Empty;
    public string UnknownField { get; set; } = string.Empty;
    public bool IsRevoked { get; set; }
    public string Message { get; set; } = string.Empty;
    public string CertificatePath {get; set; } = string.Empty;
    public DateTime ExpiryDate { get; set; } = DateTime.MinValue;
    public DateTime? RevokeDate { get; set; }
}