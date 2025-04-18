using DataGateCertManager.Models;
using DataGateCertManager.Models.Enums;

namespace DataGateCertManager.Services.Interfaces;

public interface ICertVpnService
{
    Task<List<CertificateCaInfo>> GetAllVpnServerCertificates(string  pkiPath, 
        CancellationToken cancellationToken);
    Task<CertificateBuildResult> AddServerCertificate(string easyRsaPath, string commonName,
        CancellationToken cancellationToken);
    Task<CertificateRevokeResult> RevokeServerCertificate(string easyRsaPath, string commonName,
        CancellationToken cancellationToken);
}