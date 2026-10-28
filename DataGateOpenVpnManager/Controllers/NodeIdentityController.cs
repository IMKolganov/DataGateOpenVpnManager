using DataGateMonitor.SharedModels.DataGateOpenVpnManager.LocalIssuance.Responses;
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
    public ActionResult<ApiResponse<NodeIdentityResponse>> Get()
    {
        if (!options.Enabled)
            return NotFound(ApiResponse<NodeIdentityResponse>.ErrorResponse("Local issuance is disabled on this node."));

        var identity = services.GetRequiredService<INodeIdentity>();
        return Ok(ApiResponse<NodeIdentityResponse>.SuccessResponse(new NodeIdentityResponse { Fingerprint = identity.Fingerprint, PublicKeyPem = identity.PublicKeyPem }));
    }
}
