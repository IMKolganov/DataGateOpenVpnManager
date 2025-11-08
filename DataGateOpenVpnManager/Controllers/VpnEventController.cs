using DataGateOpenVpnManager.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using OpenVPNGateMonitor.SharedModels.DataGateOpenVpnManager.VpnEvent.Requests;

namespace DataGateOpenVpnManager.Controllers;

[ApiController]
[Route("api/vpn-events")]
public class VpnEventController(
    ILogger<VpnEventController> logger,
    IHubContext<OpenVpnEventHub> hubContext) : ControllerBase
{
    [HttpPost("connect")]
    public async Task<IActionResult> OnClientConnect([FromBody] VpnEventRequest  data, CancellationToken ct)
    {
        logger.LogInformation("Received connect event: CN={CommonName}, IP={RealAddress}", data.CommonName, data.RealAddress);

        try { await hubContext.Clients.All.SendAsync("ClientConnected", data, ct); }
        catch (Exception ex) { logger.LogError(ex, "Broadcast 'ClientConnected' failed."); }

        return Ok();
    }

    [HttpPost("disconnect")]
    public async Task<IActionResult> OnClientDisconnect([FromBody] VpnEventRequest  data, CancellationToken ct)
    {
        logger.LogInformation("Received disconnect event: CN={CommonName}, Duration={DurationSec}", data.CommonName, 
            data.DurationSec);

        try { await hubContext.Clients.All.SendAsync("ClientDisconnected", data, ct); }
        catch (Exception ex) { logger.LogError(ex, "Broadcast 'ClientDisconnected' failed."); }

        return Ok();
    }

    [HttpPost("attempt")]
    public async Task<IActionResult> OnClientAttempt([FromBody] VpnEventRequest  data, CancellationToken ct)
    {
        logger.LogInformation("Received attempt event: CN={CommonName}, VirtualAddress={VirtualAddress}", data.CommonName, data.VirtualAddress);
        
        try { await hubContext.Clients.All.SendAsync("ClientAttempted", data, ct); }
        catch (Exception ex) { logger.LogError(ex, "Broadcast 'ClientAttempted' failed."); }

        return Ok();
    }

    [HttpPost("tlsverify")]
    public async Task<IActionResult> OnTlsVerify([FromBody] VpnEventRequest data, CancellationToken ct)
    {
        logger.LogInformation("TLS verified: CN={CommonName}, Message={Message}", data.CommonName, data.Message);

        try { await hubContext.Clients.All.SendAsync("TlsVerified", data, ct); }
        catch (Exception ex) { logger.LogError(ex, "Broadcast 'TlsVerified' failed."); }

        return Ok();
    }

    [HttpPost("error")]
    public async Task<IActionResult> OnError([FromBody] VpnEventRequest data, CancellationToken ct)
    {
        var type = string.IsNullOrWhiteSpace(data.EventType) ? "Error" : data.EventType;

        logger.LogInformation(
            "VPN error received: Type={EventType}, CN={CommonName}, Msg={Message}",
            type, data.CommonName, data.Message);

        try
        {
            // Unified error event (recommended to use on the frontend)
            await hubContext.Clients.All.SendAsync("ErrorEvent", data, ct);

            // Backward/typed events (optional, keep if you already use them)
            switch (type)
            {
                case "AuthFailed":
                    await hubContext.Clients.All.SendAsync("AuthFailed", data, ct);
                    break;
                case "TlsError":
                    await hubContext.Clients.All.SendAsync("TlsError", data, ct);
                    break;
                case "VerifyError":
                    await hubContext.Clients.All.SendAsync("VerifyError", data, ct);
                    break;
                default:
                    await hubContext.Clients.All.SendAsync("VpnError", data, ct);
                    break;
            }

            logger.LogInformation("Broadcast error events completed successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Broadcast error events failed.");
        }

        return Ok();
    }
}
