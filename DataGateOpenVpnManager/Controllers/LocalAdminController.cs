using System.Text;
using DataGateMonitor.SharedModels.Responses;
using DataGateOpenVpnManager.Services.LocalIssuance;
using Microsoft.AspNetCore.Mvc;

namespace DataGateOpenVpnManager.Controllers;

/// <summary>
/// Node-local profile issuance. Reachable only over the admin Unix socket (see <see cref="LocalAdminChannel"/>);
/// use <c>datagate-profile</c> inside the container.
/// </summary>
[ApiController]
[Route("api/local-admin")]
[LocalAdminOnly]
[ApiExplorerSettings(IgnoreApi = true)]
public class LocalAdminController(
    ILocalProfileService localProfileService,
    INodeIdentity nodeIdentity,
    ILocalIssuanceOutbox outbox)
    : ControllerBase
{
    [HttpGet("profiles")]
    public async Task<IActionResult> List([FromQuery] string? format, CancellationToken cancellationToken) =>
        await Handle(async () =>
        {
            var profiles = await localProfileService.ListAsync(cancellationToken);
            return string.Equals(format, "text", StringComparison.OrdinalIgnoreCase)
                ? Content(FormatTable(profiles), "text/plain", Encoding.UTF8)
                : Ok(ApiResponse<IReadOnlyList<LocalProfileView>>.SuccessResponse(profiles));
        });

    [HttpPost("profiles")]
    public async Task<IActionResult> Create([FromBody] LocalProfileCreateRequest request, CancellationToken cancellationToken) =>
        await Handle(async () =>
            Ok(ApiResponse<LocalProfileView>.SuccessResponse(await localProfileService.CreateAsync(request, cancellationToken))));

    [HttpPost("profiles/{commonName}/revoke")]
    public async Task<IActionResult> Revoke([FromRoute] string commonName, CancellationToken cancellationToken) =>
        await Handle(async () =>
            Ok(ApiResponse<LocalProfileView>.SuccessResponse(await localProfileService.RevokeAsync(commonName, cancellationToken))));

    [HttpGet("profiles/{commonName}/ovpn")]
    public async Task<IActionResult> Ovpn([FromRoute] string commonName, CancellationToken cancellationToken) =>
        await Handle(async () =>
            Content(await localProfileService.GetOvpnAsync(commonName, cancellationToken), "application/x-openvpn-profile", Encoding.UTF8));

    [HttpGet("node-identity")]
    public IActionResult NodeIdentity() => Ok(ApiResponse<object>.SuccessResponse(new
    {
        nodeIdentity.Fingerprint,
        nodeIdentity.PublicKeyPem,
        PendingEvents = outbox.ListPending().Count,
    }));

    private async Task<IActionResult> Handle(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (LocalIssuanceValidationException ex)
        {
            var body = ApiResponse<object>.ErrorResponse(ex.Message);
            return ex.IsConflict ? Conflict(body) : BadRequest(body);
        }
    }

    private static string FormatTable(IReadOnlyList<LocalProfileView> profiles)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{"COMMON NAME",-32} {"ORIGIN",-9} {"STATUS",-8} {"SERVICE",-7} {"STATIC IP",-15} SERIAL");
        foreach (var p in profiles)
            sb.AppendLine(
                $"{p.CommonName,-32} {p.Origin,-9} {p.Status,-8} {(p.IsServiceProfile ? "yes" : "no"),-7} {p.StaticIp ?? "-",-15} {p.SerialNumber}");
        return sb.ToString();
    }
}
