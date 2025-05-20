using DataGateCertManager.Helpers;
using DataGateCertManager.Services.EasyRsaServices.Interfaces;
using Microsoft.AspNetCore.Mvc;
using OpenVPNGateMonitor.SharedModels.DataGateCertManager.Cert.Requests;
using OpenVPNGateMonitor.SharedModels.DataGateCertManager.Cert.Responses;

namespace DataGateCertManager.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CertController(
    IEasyRsaService easyRsaService,
    IEasyRsaPathResolver easyRsaPathResolver,
    ILogger<CertController> logger)
    : ControllerBase
{
    [HttpGet("GetAllCertificates")]
    public async Task<ActionResult<List<ServerCertificate>>> GetAllCertificates()
    {
        try
        {
            var mainPath = easyRsaPathResolver.GetEasyRsaPath();

            var certificates = await easyRsaService.GetAllCertificateInfoInIndexFileAsync(
                mainPath,
                HttpContext.RequestAborted);

            return Ok(certificates);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting all certificates");
            return BadRequest(new { error = ex.Message });
        }
    }
    
    [HttpPost("AddServerCertificate")]
    public async Task<ActionResult<ServerCertificate>> AddServerCertificate(
        [FromBody] AddServerCertificateRequest request)
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
                HttpContext.RequestAborted,
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

    [HttpPost("RevokeCertificate")]
    public async Task<ActionResult<ServerCertificate>> RevokeCertificate([FromBody] 
        RevokeServerCertificateRequest request)
    {
        try
        {
            var mainPath = easyRsaPathResolver.GetEasyRsaPath();

            var result = await easyRsaService.RevokeCertificateAsync(
                mainPath,
                request.CommonName,
                HttpContext.RequestAborted);

            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error revoking certificate for {CommonName}", request.CommonName);
            return BadRequest(new { error = ex.Message });
        }
    }
}