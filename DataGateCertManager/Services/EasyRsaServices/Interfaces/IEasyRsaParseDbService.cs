using DataGateCertManager.Models;

namespace DataGateCertManager.Services.EasyRsaServices.Interfaces;

public interface IEasyRsaParseDbService
{
    List<CertificateCaInfo> ParseCertificateInfoInIndexFile(string pkiPath);
}