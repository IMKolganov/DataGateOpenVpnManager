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

    public async Task<List<CertificateCaInfo>> GetAllVpnServerCertificates(int vpnServerId, 
        CancellationToken cancellationToken)
    {
        var openVpnServerCertConfig = await GetOpenVpnServerCertConf(vpnServerId, cancellationToken);

        if (openVpnServerCertConfig == null)
            throw new InvalidOperationException();

        return _easyRsaService
            .GetAllCertificateInfoInIndexFile(openVpnServerCertConfig.PkiPath)
            .Select(cert =>
            {
                cert.VpnServerId = vpnServerId;
                return cert;
            })
            .ToList();
    }
    
    public async Task<List<CertificateCaInfo>> GetAllVpnServerCertificatesByStatus(int vpnServerId, 
        CertificateStatus certificateStatus, CancellationToken cancellationToken)
    {
        var openVpnServerCertConfig = await GetOpenVpnServerCertConf(vpnServerId, cancellationToken);

        return _easyRsaService.GetAllCertificateInfoInIndexFile(openVpnServerCertConfig.PkiPath)
            .Where(x => x.Status == certificateStatus)
            .Select(x =>
            {
                x.VpnServerId = vpnServerId;
                return x;
            })
            .ToList();
    }

    public async Task<CertificateBuildResult> AddServerCertificate(int vpnServerId, string commonName, 
        CancellationToken cancellationToken)
    {
        var openVpnServerCertConfig = await GetOpenVpnServerCertConf(vpnServerId, cancellationToken);
        //first realization, with "nopass", without any params if you need more check method BuildCertificate
        return _easyRsaService.BuildCertificate(openVpnServerCertConfig, commonName);
    }
    
    public async Task<CertificateRevokeResult> RevokeServerCertificate(int vpnServerId, string commonName, 
        CancellationToken cancellationToken)
    {
        var openVpnServerCertConfig = await GetOpenVpnServerCertConf(vpnServerId, cancellationToken);
        return _easyRsaService.RevokeCertificate(openVpnServerCertConfig, commonName);
    }

    // public async Task<OpenVpnServerCertConfig> GetOpenVpnServerCertConf(int vpnServerId, 
    //     CancellationToken cancellationToken)
    // {
    //     return await _unitOfWork.GetQuery<OpenVpnServerCertConfig>()
    //         .AsQueryable()
    //         .Where(x => x.VpnServerId == vpnServerId)
    //         .FirstOrDefaultAsync(cancellationToken) ?? 
    //            new OpenVpnServerCertConfig() { VpnServerId = vpnServerId };
    // }

    public async Task<OpenVpnServerCertConfig> UpdateServerCertConfig(
        OpenVpnServerCertConfigInfo openVpnServerCertConfigInfo,
        CancellationToken cancellationToken)
    {
        var repositoryOpenVpnServerCertConfig = _unitOfWork.GetRepository<OpenVpnServerCertConfig>();

        var exstOpenVpnServerCertConfig = await _unitOfWork.GetQuery<OpenVpnServerCertConfig>()
            .AsQueryable()
            .Where(x => x.VpnServerId == openVpnServerCertConfigInfo.VpnServerId)
            .FirstOrDefaultAsync(cancellationToken);

        OpenVpnServerCertConfig resultConfig;

        if (exstOpenVpnServerCertConfig != null)
        {
            exstOpenVpnServerCertConfig.EasyRsaPath = openVpnServerCertConfigInfo.EasyRsaPath;
            exstOpenVpnServerCertConfig.OvpnFileDir = openVpnServerCertConfigInfo.OvpnFileDir;
            exstOpenVpnServerCertConfig.RevokedOvpnFilesDirPath =
                openVpnServerCertConfigInfo.RevokedOvpnFilesDirPath;
            exstOpenVpnServerCertConfig.PkiPath = openVpnServerCertConfigInfo.PkiPath;
            exstOpenVpnServerCertConfig.CaCertPath = openVpnServerCertConfigInfo.CaCertPath;
            exstOpenVpnServerCertConfig.TlsAuthKey = openVpnServerCertConfigInfo.TlsAuthKey;
            exstOpenVpnServerCertConfig.ServerRemoteIp = openVpnServerCertConfigInfo.ServerRemoteIp;
            exstOpenVpnServerCertConfig.CrlPkiPath = openVpnServerCertConfigInfo.CrlPkiPath;
            exstOpenVpnServerCertConfig.CrlOpenvpnPath = openVpnServerCertConfigInfo.CrlOpenvpnPath;
            exstOpenVpnServerCertConfig.StatusFilePath = openVpnServerCertConfigInfo.StatusFilePath;

            repositoryOpenVpnServerCertConfig.Update(exstOpenVpnServerCertConfig);
            resultConfig = exstOpenVpnServerCertConfig;
        }
        else
        {
            resultConfig = new OpenVpnServerCertConfig
            {
                VpnServerId = openVpnServerCertConfigInfo.VpnServerId,
                EasyRsaPath = openVpnServerCertConfigInfo.EasyRsaPath,
                OvpnFileDir = openVpnServerCertConfigInfo.OvpnFileDir,
                RevokedOvpnFilesDirPath = openVpnServerCertConfigInfo.RevokedOvpnFilesDirPath,
                PkiPath = openVpnServerCertConfigInfo.PkiPath,
                CaCertPath = openVpnServerCertConfigInfo.CaCertPath,
                TlsAuthKey = openVpnServerCertConfigInfo.TlsAuthKey,
                ServerRemoteIp = openVpnServerCertConfigInfo.ServerRemoteIp,
                CrlPkiPath = openVpnServerCertConfigInfo.CrlPkiPath,
                CrlOpenvpnPath = openVpnServerCertConfigInfo.CrlOpenvpnPath,
                StatusFilePath = openVpnServerCertConfigInfo.StatusFilePath
            };
            await repositoryOpenVpnServerCertConfig.AddAsync(resultConfig, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return resultConfig;
    }
}