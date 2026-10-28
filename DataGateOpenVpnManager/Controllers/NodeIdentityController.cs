using DataGateMonitor.SharedModels.Responses;
using DataGateOpenVpnManager.Services.LocalIssuance;
using Microsoft.AspNetCore.Mvc;

namespace DataGateOpenVpnManager.Controllers;

/// <summary>
/// Backend-JWT endpoint: lets the backend fetch this node's event-signing public key over the channel it
/// already trusts (used for nodes registered before local issuance existed).
/// </summary>
[ApiController]
[Route("api/node-identity")]
public class NodeIdentityController(LocalIssuanceOptions options, IServiceProvider services) : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        if (!options.Enabled)
            return NotFound(ApiResponse<object>.ErrorResponse("Local issuance is disabled on this node."));

        var identity = services.GetRequiredService<INodeIdentity>();
        return Ok(ApiResponse<object>.SuccessResponse(new { identity.Fingerprint, identity.PublicKeyPem }));
    }
}
