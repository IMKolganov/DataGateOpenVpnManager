using DataGateCertManager.Models;
using DataGateCertManager.Models.Enums;

namespace DataGateCertManager.Services.Interfaces;

public interface ICertVpnService
{
    Task<List<CertificateCaInfo>> GetAllVpnServerCertificates(int vpnServerId,
        CancellationToken cancellationToken);
    Task<List<CertificateCaInfo>> GetAllVpnServerCertificatesByStatus(int vpnServerId,
        CertificateStatus certificateStatus, CancellationToken cancellationToken);
    Task<CertificateBuildResult> AddServerCertificate(int vpnServerId, string commonName,
        CancellationToken cancellationToken);
    Task<CertificateRevokeResult> RevokeServerCertificate(int vpnServerId, string commonName,
        CancellationToken cancellationToken);
    Task<OpenVpnServerCertConfig> GetOpenVpnServerCertConf(int vpnServerId,
        CancellationToken cancellationToken);
    Task<OpenVpnServerCertConfig> UpdateServerCertConfig(
        OpenVpnServerCertConfigInfo openVpnServerCertConfigInfo,
        CancellationToken cancellationToken);
}