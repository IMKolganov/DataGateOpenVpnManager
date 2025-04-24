using DataGateCertManager.Models.Dto;

namespace DataGateCertManager.Services.EasyRsaServices.Interfaces;

public interface IEasyRsaService
{
    Task<ServerCertificate> BuildCertificate(string easyRsaPath, CancellationToken cancellationToken,
        string baseFileName = "client1");
    Task<string> ReadPemContent(string filePath, CancellationToken cancellationToken);
    Task<ServerCertificate> RevokeCertificate(string easyRsaPath, string commonName,
        CancellationToken cancellationToken);
    Task<List<ServerCertificate>> GetAllCertificateInfoInIndexFile(string easyRsaPath, CancellationToken cancellationToken);
}