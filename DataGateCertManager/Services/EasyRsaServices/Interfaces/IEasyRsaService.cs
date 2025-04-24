using DataGateCertManager.Models.Dto;

namespace DataGateCertManager.Services.EasyRsaServices.Interfaces;

public interface IEasyRsaService
{
    Task<ServerCertificate> BuildCertificateAsync(string easyRsaPath, CancellationToken cancellationToken,
        string baseFileName = "client1", int certExpireDays = 365);
    Task<string> ReadPemContentAsync(string filePath, CancellationToken cancellationToken);
    Task<ServerCertificate> RevokeCertificateAsync(string easyRsaPath, string commonName,
        CancellationToken cancellationToken);
    Task<List<ServerCertificate>> GetAllCertificateInfoInIndexFileAsync(string easyRsaPath,
        CancellationToken cancellationToken);
}