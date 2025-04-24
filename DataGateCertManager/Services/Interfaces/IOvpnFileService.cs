using DataGateCertManager.Models;
using DataGateCertManager.Models.Dto;
using DataGateCertManager.Models.Enums;

namespace DataGateCertManager.Services.Interfaces;

public interface IOvpnFileService
{
    Task<IssuedOvpnFile> AddOvpnFile(string easyRsaPath, string commonName, string configTemplate, 
        string serverIp, int serverPort, CancellationToken cancellationToken, string issuedTo = "openVpnClient");
    Task<IssuedOvpnFile?> RevokeOvpnFile(string easyRsaPath, string commonName, 
        CancellationToken cancellationToken);
    Task<OvpnFile> GetOvpnFile(string fileName, string filePath, CancellationToken cancellationToken);
}