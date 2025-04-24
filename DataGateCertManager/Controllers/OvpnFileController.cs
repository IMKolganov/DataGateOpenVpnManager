using DataGateCertManager.Models;
using DataGateCertManager.Services.EasyRsaServices.Interfaces;
using DataGateCertManager.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DataGateCertManager.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OvpnFileController(
    IEasyRsaService easyRsaService,
    IOvpnFileService ovpnFileService,
    IConfiguration configuration,
    ILogger<OvpnFileController> logger)
    : ControllerBase
{
    [HttpPost("AddOvpnFile")]
    public async Task<IActionResult> AddOvpnFile([FromBody] AddOvpnFileRequest request,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
        var mainPath = configuration["EasyRsa:MainPath"] 
                       ?? throw new InvalidOperationException("EasyRsa:MainPath configuration is missing");
        // ovpnFileService.AddOvpnFile()
    }

    [HttpPost("RevokeOvpnFile")]
    public async Task<IActionResult> RevokeOvpnFile([FromBody] RevokeOvpnFileRequest request,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    [HttpPost("DownloadOvpnFile/")]
    public async Task<IActionResult> DownloadOvpnFile(
        [FromBody] DownloadOvpnFileRequest request,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}

