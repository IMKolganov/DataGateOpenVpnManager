using DataGateCertManager.Models;
using DataGateCertManager.Services.EasyRsaServices.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DataGateCertManager.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OvpnFileController(
    IEasyRsaService easyRsaService,
    IConfiguration configuration,
    ILogger<OvpnFileController> logger)
    : ControllerBase
{
    [HttpPost("AddOvpnFile")]
    public async Task<IActionResult> AddOvpnFile([FromBody] AddOvpnFileRequest request,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
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

