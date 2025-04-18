using DataGateCertManager.Models;

namespace DataGateCertManager.Services.EasyRsaServices.Interfaces;

public interface IEasyRsaService
{
    Task<CertificateBuildResult> BuildCertificate(string easyRsaPath, CancellationToken cancellationToken,
        string baseFileName = "client1");
    Task<string> ReadPemContent(string filePath, CancellationToken cancellationToken);
    Task<CertificateRevokeResult> RevokeCertificate(string easyRsaPath, string commonName,
        CancellationToken cancellationToken);
    Task<List<CertificateCaInfo>> GetAllCertificateInfoInIndexFile(string pkiPath, CancellationToken cancellationToken);
}