using DataGateCertManager.Models;
using DataGateCertManager.Models.Dto;

namespace DataGateCertManager.Services.EasyRsaServices.Interfaces;

public interface IEasyRsaParseDbService
{
    Task<List<ServerCertificate>> ParseCertificateInfoInIndexFileAsync(string pkiPath,
        CancellationToken cancellationToken);
}