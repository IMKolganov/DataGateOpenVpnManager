using OpenVPNGateMonitor.SharedModels.DataGateOpenVpnManager.Cert.Responses;

namespace DataGateOpenVpnManager.Services.EasyRsaServices.Interfaces;

public interface IEasyRsaParseDbService
{
    Task<List<ServerCertificate>> ParseCertificateInfoInIndexFileAsync(string pkiPath,
        CancellationToken cancellationToken);
}