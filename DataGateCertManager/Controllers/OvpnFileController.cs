using DataGateCertManager.Helpers;
using DataGateCertManager.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using OpenVPNGateMonitor.SharedModels.DataGateCertManager.OvpnFile.Requests;
using OpenVPNGateMonitor.SharedModels.DataGateCertManager.OvpnFile.Responses;

namespace DataGateCertManager.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OvpnFileController(
    IOvpnFileService ovpnFileService,
    IEasyRsaPathResolver easyRsaPathResolver,
    ILogger<OvpnFileController> logger)
    : ControllerBase
{
    [HttpPost("AddOvpnFile")]
    public async Task<ActionResult<OvpnFileMetadata>> AddOvpnFile([FromBody] GenerateOvpnFileRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var mainPath = easyRsaPathResolver.GetEasyRsaPath();

            if (string.IsNullOrEmpty(request.CommonName) || string.IsNullOrEmpty(request.ConfigTemplate))//todo: fix
            {
                throw new NullReferenceException("Common name and config template are required");
            }

            var result = await ovpnFileService.AddOvpnFile(
                mainPath,
                request.CommonName,
                request.FriendlyΝame,
                request.ConfigTemplate,
                request.ServerIp,
                request.ServerPort,
                cancellationToken,
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
    public async Task<ActionResult<OvpnFileMetadata>> RevokeOvpnFile([FromBody] RevokeOvpnFileRequest request, 
        CancellationToken cancellationToken)
    {
        try
        {
            var mainPath = easyRsaPathResolver.GetEasyRsaPath();

            var result = await ovpnFileService.RevokeOvpnFile(
                mainPath,
                request.CommonName,
                request.OvpnFileName,
                request.OvpnFilePath,
                cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error revoke ovpn file for {CommonName}", request.CommonName);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("DownloadOvpnFile")]
    public async Task<ActionResult<OvpnFileDownload>> DownloadOvpnFile([FromBody] DownloadOvpnFileRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await ovpnFileService.GetOvpnFile(
                request.FileName,
                request.FilePath,
                cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error download ovpn file for {CommonName}", request.CommonName);
            return BadRequest(new { error = ex.Message });
        }
    }
}

