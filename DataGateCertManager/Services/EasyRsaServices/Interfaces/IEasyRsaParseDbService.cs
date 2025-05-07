using OpenVPNGateMonitor.SharedModels.DataGateCertManager.Cert.Responses;

namespace DataGateCertManager.Services.EasyRsaServices.Interfaces;

public interface IEasyRsaParseDbService
{
    Task<List<ServerCertificate>> ParseCertificateInfoInIndexFileAsync(string pkiPath,
        CancellationToken cancellationToken);
}