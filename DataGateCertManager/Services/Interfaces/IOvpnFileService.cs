using DataGateCertManager.Models;
using DataGateCertManager.Models.Dto;
using DataGateCertManager.Models.Enums;

namespace DataGateCertManager.Services.Interfaces;

public interface IOvpnFileService
{
    Task<IssuedOvpnFile> AddOvpnFile(string externalId, string commonName, string ovpnFileDir, 
        CancellationToken cancellationToken, string issuedTo = "openVpnClient");
    Task<IssuedOvpnFile?> RevokeOvpnFile(string easyRsaPath, string commonName, 
        CancellationToken cancellationToken);
    Task<OvpnFile> GetOvpnFile(string fileName, string filePath, CancellationToken cancellationToken);
}