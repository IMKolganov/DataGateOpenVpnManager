using DataGateCertManager.Models;
using DataGateCertManager.Models.Dto;
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
    public async Task<ActionResult<IssuedOvpnFile>> AddOvpnFile([FromBody] AddOvpnFileRequest request,
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

    [HttpPost("RevokeOvpnFile/{commonName}")]
    public async Task<IActionResult> RevokeOvpnFile(string commonName)
    {
        try
        {
            var mainPath = configuration["EasyRsa:MainPath"] 
                           ?? throw new InvalidOperationException("EasyRsa:MainPath configuration is missing");

            var result = await ovpnFileService.RevokeOvpnFile(
                mainPath,
                commonName,
                HttpContext.RequestAborted);

            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error revoke ovpn file for {CommonName}", commonName);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("DownloadOvpnFile")]
    public async Task<IActionResult> DownloadOvpnFile(
        [FromBody] DownloadOvpnFileRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var mainPath = configuration["EasyRsa:MainPath"] 
                           ?? throw new InvalidOperationException("EasyRsa:MainPath configuration is missing");

            var result = await ovpnFileService.GetOvpnFile(
                request.FileName,
                request.FilePath,
                HttpContext.RequestAborted);

            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error download ovpn file for {CommonName}", request.CommonName);
            return BadRequest(new { error = ex.Message });
        }
    }
}

