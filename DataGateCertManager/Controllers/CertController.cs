using DataGateCertManager.Models;
using DataGateCertManager.Models.Dto;
using DataGateCertManager.Services.EasyRsaServices.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DataGateCertManager.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CertController(
    IEasyRsaService easyRsaService,
    IConfiguration configuration,
    ILogger<CertController> logger)
    : ControllerBase
{
    [HttpGet("GetAllCertificates")]
    public async Task<ActionResult<List<ServerCertificate>>> GetAllCertificates()
    {
        try
        {
            var pkiPath = configuration["EasyRsa:MainPath"] 
                          ?? throw new InvalidOperationException("EasyRsa:PkiPath configuration is missing");

            var certificates = await easyRsaService.GetAllCertificateInfoInIndexFileAsync(
                pkiPath,
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
    public async Task<ActionResult<AddServerCertificateResponse>> AddServerCertificate(
        [FromBody] AddServerCertificateRequest request)
    {
        try
        {
            var easyRsaPath = configuration["EasyRsa:MainPath"] 
                ?? throw new InvalidOperationException("EasyRsa:MainPath configuration is missing");
            
            if (request.CertExpireDays <= 0)
            {
                request.CertExpireDays = 365;
            }

            var result = await easyRsaService.BuildCertificateAsync(
                easyRsaPath,
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

    [HttpPost("RevokeCertificate/{commonName}")]
    public async Task<ActionResult<CertificateRevokeResponse>> RevokeCertificate(string commonName)
    {
        try
        {
            var easyRsaPath = configuration["EasyRsa:MainPath"] 
                ?? throw new InvalidOperationException("EasyRsa:MainPath configuration is missing");

            var result = await easyRsaService.RevokeCertificateAsync(
                easyRsaPath,
                commonName,
                HttpContext.RequestAborted);

            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error revoking certificate for {CommonName}", commonName);
            return BadRequest(new { error = ex.Message });
        }
    }
}