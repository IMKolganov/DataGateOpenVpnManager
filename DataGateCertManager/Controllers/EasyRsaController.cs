using DataGateCertManager.Models;
using DataGateCertManager.Services.EasyRsaServices.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DataGateCertManager.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EasyRsaController : ControllerBase
{
    private readonly IEasyRsaService _easyRsaService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EasyRsaController> _logger;

    public EasyRsaController(
        IEasyRsaService easyRsaService,
        IConfiguration configuration,
        ILogger<EasyRsaController> logger)
    {
        _easyRsaService = easyRsaService;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("certificates")]
    public async Task<ActionResult<CertificateBuildResult>> BuildCertificate([FromBody] CertificateBuildRequest request)
    {
        try
        {
            var easyRsaPath = _configuration["EasyRsa:Path"] 
                ?? throw new InvalidOperationException("EasyRsa:Path configuration is missing");

            var result = await _easyRsaService.BuildCertificate(
                easyRsaPath,
                HttpContext.RequestAborted,
                request.CommonName);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building certificate for {CommonName}", request.CommonName);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("certificates/{commonName}/revoke")]
    public async Task<ActionResult<CertificateRevokeResult>> RevokeCertificate(string commonName)
    {
        try
        {
            var easyRsaPath = _configuration["EasyRsa:Path"] 
                ?? throw new InvalidOperationException("EasyRsa:Path configuration is missing");

            var result = await _easyRsaService.RevokeCertificate(
                easyRsaPath,
                commonName,
                HttpContext.RequestAborted);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking certificate for {CommonName}", commonName);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("certificates")]
    public async Task<ActionResult<List<CertificateCaInfo>>> GetAllCertificates()
    {
        try
        {
            var pkiPath = _configuration["EasyRsa:PkiPath"] 
                ?? throw new InvalidOperationException("EasyRsa:PkiPath configuration is missing");

            var certificates = await _easyRsaService.GetAllCertificateInfoInIndexFile(
                pkiPath,
                HttpContext.RequestAborted);

            return Ok(certificates);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all certificates");
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("certificates/{filePath}/pem")]
    public async Task<ActionResult<string>> GetPemContent([FromRoute] string filePath)
    {
        try
        {
            var content = await _easyRsaService.ReadPemContent(filePath, HttpContext.RequestAborted);
            return Ok(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading PEM content from {FilePath}", filePath);
            return BadRequest(new { error = ex.Message });
        }
    }
}