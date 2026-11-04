using DataGateOpenVpnManager.Services.LocalIssuance;
using Microsoft.AspNetCore.Mvc;

namespace DataGateOpenVpnManager.Controllers;

[ApiController]
[Route("api/local/service-clients")]
[ServiceClientLookupOnly]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class ServiceClientsController(IServiceClientLookup lookup) : ControllerBase
{
    [HttpGet("by-virtual-ip/{ip}")]
    public async Task<IActionResult> ByVirtualIp(string ip, CancellationToken cancellationToken)
    {
        var client = await lookup.FindAsync(ip, cancellationToken);
        return client is null ? NotFound() : Ok(client);
    }
}
