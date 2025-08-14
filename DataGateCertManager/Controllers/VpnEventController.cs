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
        logger.LogInformation("Received connect event: CN={CommonName}, IP={RealAddress}", data.CommonName, data.RealAddress);
        try
        {
            await hubContext.Clients.All.SendAsync("ClientConnected", data);
            logger.LogInformation("Broadcast 'ClientConnected' completed successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Broadcast 'ClientConnected' failed.");
        }
        return Ok();
    }

    [HttpPost("disconnect")]
    public async Task<IActionResult> OnClientDisconnect([FromBody] VpnEventData data)
    {
        logger.LogInformation("Received disconnect event: CN={CommonName}, Duration={ConnectedSince}", data.CommonName, data.ConnectedSince);
        try
        {
            await hubContext.Clients.All.SendAsync("ClientDisconnected", data);
            logger.LogInformation("Broadcast 'ClientDisconnected' completed successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Broadcast 'ClientDisconnected' failed.");
        }
        return Ok();
    }

    [HttpPost("attempt")]
    public async Task<IActionResult> OnClientAttempt([FromBody] VpnEventData data)
    {
        logger.LogInformation("Received attempt event: CN={CommonName}, VirtualAddress={VirtualAddress}", data.CommonName, data.VirtualAddress);
        try
        {
            await hubContext.Clients.All.SendAsync("ClientAttempted", data);
            logger.LogInformation("Broadcast 'ClientAttempted' completed successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Broadcast 'ClientAttempted' failed.");
        }
        return Ok();
    }

    [HttpPost("tlsverify")]
    public async Task<IActionResult> OnTlsVerify([FromBody] VpnEventData data)
    {
        logger.LogInformation("TLS verified: CN={CommonName}, Message={Message}", data.CommonName, data.Message);
        try
        {
            await hubContext.Clients.All.SendAsync("TlsVerified", data);
            logger.LogInformation("Broadcast 'TlsVerified' completed successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Broadcast 'TlsVerified' failed.");
        }
        return Ok();
    }
    
    [HttpPost("authfail")]
    public async Task<IActionResult> OnAuthFailed([FromBody] VpnEventData data)
    {
        logger.LogInformation("Auth failed: CN={CommonName}, Message={Message}", data.CommonName, data.Message);
        try
        {
            await hubContext.Clients.All.SendAsync("AuthFailed", data);
            logger.LogInformation("Broadcast 'AuthFailed' completed successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Broadcast 'AuthFailed' failed.");
        }
        return Ok();
    }
    
    [HttpPost("envdump")]
    public async Task<IActionResult> EnvDump([FromBody] VpnEnvDump data)
    {
        logger.LogInformation(
            "Received env dump from hook '{Hook}' at {Timestamp}. Args={ArgsCount}, Env length={EnvLength}",
            data.Hook, data.Timestamp, data.Args?.Count ?? 0, data.EnvB64?.Length ?? 0);

        if (!string.IsNullOrWhiteSpace(data.EnvB64))
        {
            try
            {
                var envText = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(data.EnvB64));
                logger.LogInformation("Decoded environment for {Hook}:\n{Env}", data.Hook, envText);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to decode EnvB64 for hook {Hook}", data.Hook);
            }
        }

        try
        {
            await hubContext.Clients.All.SendAsync("EnvDumpReceived", data);
            logger.LogInformation("Broadcast 'EnvDumpReceived' completed successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Broadcast 'EnvDumpReceived' failed.");
        }

        return Ok();
    }
}
