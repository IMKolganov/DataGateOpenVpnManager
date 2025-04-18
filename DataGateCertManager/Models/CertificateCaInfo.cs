using DataGateCertManager.Models.Enums;

namespace DataGateCertManager.Models;

public class CertificateCaInfo
{
    public int Id { get; set; }
    public int VpnServerId { get; set; }
    public string CommonName { get; set; } = string.Empty;
    public CertificateStatus Status { get; set; } = CertificateStatus.Unknown;
    public DateTime ExpiryDate { get; set; } = DateTime.MinValue;
    public DateTime? RevokeDate { get; set; }
    public string SerialNumber { get; set; } = string.Empty;
    public string UnknownField { get; set; } = string.Empty;
}