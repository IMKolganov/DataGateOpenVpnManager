using DataGateCertManager.Models;
using DataGateCertManager.Services.EasyRsaServices.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DataGateCertManager.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EasyRsaController(
    IEasyRsaService easyRsaService,
    IConfiguration configuration,
    ILogger<EasyRsaController> logger)
    : ControllerBase
{
    [HttpPost("certificates")]
    public async Task<ActionResult<CertificateBuildResult>> BuildCertificate([FromBody] CertificateBuildRequest request)
    {
        try
        {
            var easyRsaPath = configuration["EasyRsa:Path"] 
                ?? throw new InvalidOperationException("EasyRsa:Path configuration is missing");

            var result = await easyRsaService.BuildCertificate(
                easyRsaPath,
                HttpContext.RequestAborted,
                request.CommonName);

            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error building certificate for {CommonName}", request.CommonName);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("certificates/{commonName}/revoke")]
    public async Task<ActionResult<CertificateRevokeResult>> RevokeCertificate(string commonName)
    {
        try
        {
            var easyRsaPath = configuration["EasyRsa:Path"] 
                ?? throw new InvalidOperationException("EasyRsa:Path configuration is missing");

            var result = await easyRsaService.RevokeCertificate(
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

    [HttpGet("certificates")]
    public async Task<ActionResult<List<CertificateCaInfo>>> GetAllCertificates()
    {
        try
        {
            var pkiPath = configuration["EasyRsa:PkiPath"] 
                ?? throw new InvalidOperationException("EasyRsa:PkiPath configuration is missing");

            var certificates = await easyRsaService.GetAllCertificateInfoInIndexFile(
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

    [HttpGet("certificates/{filePath}/pem")]
    public async Task<ActionResult<string>> GetPemContent([FromRoute] string filePath)
    {
        try
        {
            var content = await easyRsaService.ReadPemContent(filePath, HttpContext.RequestAborted);
            return Ok(content);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reading PEM content from {FilePath}", filePath);
            return BadRequest(new { error = ex.Message });
        }
    }
}