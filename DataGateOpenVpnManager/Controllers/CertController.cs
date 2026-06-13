using DataGateOpenVpnManager.Helpers;
using DataGateOpenVpnManager.Services.EasyRsaServices.Interfaces;
using Microsoft.AspNetCore.Mvc;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Cert.Requests;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Cert.Responses;
using DataGateMonitor.SharedModels.Responses;

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
    public async Task<ActionResult<ApiResponse<List<ServerCertificate>>>> GetAllCertificates(CancellationToken cancellationToken)
    {
        try
        {
            var mainPath = easyRsaPathResolver.GetEasyRsaPath();

            var certificates = await easyRsaService.GetAllCertificateInfoInIndexFileAsync(
                mainPath,
                cancellationToken);

            return Ok(ApiResponse<List<ServerCertificate>>.SuccessResponse(certificates));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting all certificates");
            return BadRequest(ApiResponse<List<ServerCertificate>>.ErrorResponse(ex.Message));
        }
    }
    
    [HttpPost("add")]
    public async Task<ActionResult<ApiResponse<ServerCertificate>>> AddServerCertificate(
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

            return Ok(ApiResponse<ServerCertificate>.SuccessResponse(result));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error building certificate for {CommonName}", request.CommonName);
            return BadRequest(ApiResponse<ServerCertificate>.ErrorResponse(ex.Message));
        }
    }

    [HttpPost("revoke")]
    public async Task<ActionResult<ApiResponse<ServerCertificate>>> RevokeCertificate([FromBody] 
        RevokeServerCertificateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var mainPath = easyRsaPathResolver.GetEasyRsaPath();

            var result = await easyRsaService.RevokeCertificateAsync(
                mainPath,
                request.CommonName,
                cancellationToken);

            return Ok(ApiResponse<ServerCertificate>.SuccessResponse(result));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error revoking certificate for {CommonName}", request.CommonName);
            return BadRequest(ApiResponse<ServerCertificate>.ErrorResponse(ex.Message));
        }
    }
}
