using DataGateCertManager.Hubs;
using DataGateCertManager.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace DataGateCertManager.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VpnEventController(
    ILogger<VpnEventController> logger,
    IHubContext<OpenVpnEventHub> hubContext)
    : ControllerBase
{
    [HttpPost("connect")]
    public async Task<IActionResult> OnClientConnect([FromBody] VpnEventData data)
    {
        Console.WriteLine($"Client connected: {data.CommonName}, IP: {data.RealAddress}");
        await hubContext.Clients.All.SendAsync("ClientConnected", data);
        return Ok();
    }

    [HttpPost("disconnect")]
    public async Task<IActionResult> OnClientDisconnect([FromBody] VpnEventData data)
    {
        Console.WriteLine($"Client disconnected: {data.CommonName}, Duration: {data.ConnectedSince}");
        await hubContext.Clients.All.SendAsync("ClientDisconnected", data);
        return Ok();
    }

    [HttpPost("attempt")]
    public async Task<IActionResult> OnClientAttempt([FromBody] VpnEventData data)
    {
        Console.WriteLine($"Client attempt: {data.CommonName} @ {data.VirtualAddress}");
        await hubContext.Clients.All.SendAsync("ClientAttempted", data);
        return Ok();
    }

    [HttpPost("tlsverify")]
    public async Task<IActionResult> OnTlsVerify([FromBody] VpnEventData data)
    {
        logger.LogInformation("TLS verified CN: {CommonName}, Depth: {Message}", data.CommonName, data.Message);
        await hubContext.Clients.All.SendAsync("TlsVerified", data);
        return Ok();
    }
}