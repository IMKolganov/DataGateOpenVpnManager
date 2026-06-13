using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Cert.Responses;

namespace DataGateOpenVpnManager.Services.EasyRsaServices.Interfaces;

public interface IEasyRsaService
{
    Task<ServerCertificate> BuildCertificateAsync(string easyRsaPath, CancellationToken cancellationToken,
        string baseFileName = "client1", int certExpireDays = 365);
    Task<ServerCertificate> RevokeCertificateAsync(string easyRsaPath, string commonName,
        CancellationToken cancellationToken);
    Task<List<ServerCertificate>> GetAllCertificateInfoInIndexFileAsync(string easyRsaPath,
        CancellationToken cancellationToken);
}