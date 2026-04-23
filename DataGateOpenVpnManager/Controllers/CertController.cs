using DataGateOpenVpnManager.Helpers;
using DataGateOpenVpnManager.Services.EasyRsaServices.Interfaces;
using Microsoft.AspNetCore.Mvc;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Cert.Requests;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Cert.Responses;

namespace DataGateOpenVpnManager.Controllers;

[ApiController]
[Route("api/certs")]
public class CertController(
    IEasyRsaService easyRsaService,
    IEasyRsaPathResolver easyRsaPathResolver,
    ILogger<CertController> logger)
    : ControllerBase
{
    [HttpGet("get-all")]
    public async Task<ActionResult<List<ServerCertificate>>> GetAllCertificates(CancellationToken cancellationToken)
    {
        try
        {
            var mainPath = easyRsaPathResolver.GetEasyRsaPath();

            var certificates = await easyRsaService.GetAllCertificateInfoInIndexFileAsync(
                mainPath,
                cancellationToken);

            return Ok(certificates);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting all certificates");
            return BadRequest(new { error = ex.Message });
        }
    }
    
    [HttpPost("add")]
    public async Task<ActionResult<ServerCertificate>> AddServerCertificate(
        [FromBody] AddServerCertificateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var mainPath = easyRsaPathResolver.GetEasyRsaPath();
            
            if (request.CertExpireDays <= 0)
            {
                request.CertExpireDays = 365;
            }

            var result = await easyRsaService.BuildCertificateAsync(
                mainPath,
                cancellationToken,
                request.CommonName,
                request.CertExpireDays);

            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error building certificate for {CommonName}", request.CommonName);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("revoke")]
    public async Task<ActionResult<ServerCertificate>> RevokeCertificate([FromBody] 
        RevokeServerCertificateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var mainPath = easyRsaPathResolver.GetEasyRsaPath();

            var result = await easyRsaService.RevokeCertificateAsync(
                mainPath,
                request.CommonName,
                cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error revoking certificate for {CommonName}", request.CommonName);
            return BadRequest(new { error = ex.Message });
        }
    }
}