using DataGateCertManager.Helpers;
using DataGateCertManager.Models;
using DataGateCertManager.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using OpenVPNGateMonitor.SharedModels.DataGateCertManager.OvpnFile.Requests;
using OpenVPNGateMonitor.SharedModels.DataGateCertManager.OvpnFile.Responses;

namespace DataGateCertManager.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VpnEventController(
    ILogger<VpnEventController> logger)
    : ControllerBase
{
    [HttpPost("connect")]
    public IActionResult OnClientConnect([FromBody] VpnEventData data)
    {
        // Log or process the connection
        Console.WriteLine($"Client connected: {data.CommonName}, IP: {data.RealAddress}");
        return Ok();
    }

    [HttpPost("disconnect")]
    public IActionResult OnClientDisconnect([FromBody] VpnEventData data)
    {
        // Log or process the disconnection
        Console.WriteLine($"Client disconnected: {data.CommonName}, Duration: {data.ConnectedSince}");
        return Ok();
    }
    
    [HttpPost("attempt")]
    public IActionResult OnClientAttempt([FromBody] VpnEventData data)
    {
        Console.WriteLine($"Client attempt: {data.CommonName} @ {data.VirtualAddress}");
        return Ok();
    }
    
    [HttpPost("tlsverify")]
    public IActionResult OnTlsVerify([FromBody] VpnEventData data)
    {
        logger.LogInformation("TLS verified CN: {CommonName}, Depth: {Message}", data.CommonName, data.Message);
        return Ok();
    }
}

