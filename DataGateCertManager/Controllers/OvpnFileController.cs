using DataGateCertManager.Models;
using DataGateCertManager.Services.EasyRsaServices.Interfaces;
using DataGateCertManager.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DataGateCertManager.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OvpnFileController(
    IOvpnFileService ovpnFileService,
    IConfiguration configuration,
    ILogger<OvpnFileController> logger)
    : ControllerBase
{
    [HttpPost("AddOvpnFile")]
    public async Task<IActionResult> AddOvpnFile([FromBody] AddOvpnFileRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var mainPath = configuration["EasyRsa:MainPath"] 
                           ?? throw new InvalidOperationException("EasyRsa:MainPath configuration is missing");

            var result = await ovpnFileService.AddOvpnFile(
                mainPath,
                request.CommonName,
                request.ConfigTemplate,
                request.ServerIp,
                request.ServerPort,
                HttpContext.RequestAborted,
                request.IssuedTo,
                request.OvpnFileExpireDays);

            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error added new ovpn file for {CommonName}", request.CommonName);
            return BadRequest(new { error = ex.Message });
        }
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

