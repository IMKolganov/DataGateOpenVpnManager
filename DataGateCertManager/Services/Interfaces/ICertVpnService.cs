using DataGateCertManager.Models;
using DataGateCertManager.Models.Dto;
using DataGateCertManager.Models.Enums;

namespace DataGateCertManager.Services.Interfaces;

public interface ICertVpnService
{
    Task<List<ServerCertificate>> GetAllVpnServerCertificates(string  pkiPath, 
        CancellationToken cancellationToken);
    Task<ServerCertificate> AddServerCertificate(string easyRsaPath, string commonName,
        CancellationToken cancellationToken);
    Task<ServerCertificate> RevokeServerCertificate(string easyRsaPath, string commonName,
        CancellationToken cancellationToken);
}