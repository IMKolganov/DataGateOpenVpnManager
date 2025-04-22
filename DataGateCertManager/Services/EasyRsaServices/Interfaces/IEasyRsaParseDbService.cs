using DataGateCertManager.Models;

namespace DataGateCertManager.Services.EasyRsaServices.Interfaces;

public interface IEasyRsaParseDbService
{
    Task<List<CertificateCaInfo>> ParseCertificateInfoInIndexFileAsync(string pkiPath,
        CancellationToken cancellationToken);
}