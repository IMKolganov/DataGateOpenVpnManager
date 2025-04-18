using DataGateCertManager.Models;
using DataGateCertManager.Models.Enums;
using DataGateCertManager.Services.EasyRsaServices.Interfaces;
using DataGateCertManager.Services.Interfaces;

namespace DataGateCertManager.Services;

public class CertVpnService : ICertVpnService
{
    private readonly ILogger<ICertVpnService> _logger;
    private readonly IEasyRsaService _easyRsaService;
    public CertVpnService(ILogger<ICertVpnService> logger, IEasyRsaService easyRsaService)
    {
        _logger = logger;
        _easyRsaService = easyRsaService;
    }

    public async Task<List<CertificateCaInfo>> GetAllVpnServerCertificates(string  pkiPath, 
        CancellationToken cancellationToken)
    {
        return await _easyRsaService.GetAllCertificateInfoInIndexFile(pkiPath, cancellationToken);
    }

    public async Task<CertificateBuildResult> AddServerCertificate(string easyRsaPath, string commonName, 
        CancellationToken cancellationToken)
    {
        //first realization, with "nopass", without any params if you need more check method BuildCertificate
        return await _easyRsaService.BuildCertificate(easyRsaPath, cancellationToken, commonName);
    }

    public async Task<CertificateRevokeResult> RevokeServerCertificate(string easyRsaPath, string commonName, 
        CancellationToken cancellationToken)
    {
        return await _easyRsaService.RevokeCertificate(easyRsaPath, commonName, cancellationToken);
    }
}