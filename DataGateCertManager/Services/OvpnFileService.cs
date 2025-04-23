using DataGateCertManager.Models.Dto;
using DataGateCertManager.Services.EasyRsaServices.Interfaces;
using DataGateCertManager.Services.Interfaces;

namespace DataGateCertManager.Services;

public class OvpnFileService : IOvpnFileService
{
    private readonly ILogger<IOvpnFileService> _logger;
    private readonly IEasyRsaService _easyRsaService;
    public OvpnFileService(ILogger<IOvpnFileService> logger, IEasyRsaService easyRsaService)
    {
        _logger = logger;
        _easyRsaService = easyRsaService;
    }
    
}