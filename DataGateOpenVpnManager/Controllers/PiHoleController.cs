using DataGateOpenVpnManager.Models;
using DataGateOpenVpnManager.Services.PiHole;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Diagnostics.Responses;
using DataGateMonitor.SharedModels.Responses;
using Microsoft.AspNetCore.Mvc;

namespace DataGateOpenVpnManager.Controllers;

[ApiController]
[Route("api/pi-hole")]
public class PiHoleController(
    IPiHoleRuntimeOptionsStore runtimeOptions,
    IPiHoleApiClient piHoleApiClient,
    IPiHoleCollectorStatusStore statusStore,
    IPiHoleQueryCursorStore cursorStore,
    ILogger<PiHoleController> logger) : ControllerBase
{
    [HttpGet("config")]
    public ActionResult<ApiResponse<PiHoleOptionsDto>> GetConfig()
    {
        var options = runtimeOptions.GetEffective();
        return Ok(ApiResponse<PiHoleOptionsDto>.SuccessResponse(ToDto(options)));
    }

    [HttpPut("config")]
    public ActionResult<ApiResponse<PiHoleOptionsDto>> PutConfig([FromBody] PiHoleOptionsDto request)
    {
        var current = runtimeOptions.GetEffective();
        var merged = new PiHoleOptions
        {
            Enabled = request.Enabled,
            BaseUrl = request.BaseUrl?.Trim() ?? current.BaseUrl,
            AppPassword = string.IsNullOrWhiteSpace(request.AppPassword) || request.AppPassword == "********"
                ? current.AppPassword
                : request.AppPassword.Trim(),
            PollIntervalSeconds = request.PollIntervalSeconds > 0 ? request.PollIntervalSeconds : current.PollIntervalSeconds,
            BatchSize = request.BatchSize > 0 ? request.BatchSize : current.BatchSize,
            LookbackSeconds = request.LookbackSeconds >= 0 ? request.LookbackSeconds : current.LookbackSeconds,
            ClientSubnetPrefix = request.ClientSubnetPrefix?.Trim() ?? current.ClientSubnetPrefix
        };

        runtimeOptions.Apply(merged);
        statusStore.RecordConfigApplied(DateTimeOffset.UtcNow);
        logger.LogInformation(
            "Pi-hole runtime config applied. Enabled={Enabled}, BaseUrl={BaseUrl}, PollIntervalSec={PollIntervalSec}, BatchSize={BatchSize}, HasPassword={HasPassword}",
            merged.Enabled,
            merged.BaseUrl,
            merged.PollIntervalSeconds,
            merged.BatchSize,
            !string.IsNullOrEmpty(merged.AppPassword));

        return Ok(ApiResponse<PiHoleOptionsDto>.SuccessResponse(ToDto(merged)));
    }

    [HttpGet("diagnostics")]
    public async Task<ActionResult<ApiResponse<PiHoleDiagnosticsResponse>>> GetDiagnostics(
        CancellationToken cancellationToken)
    {
        try
        {
            var options = runtimeOptions.GetEffective();
            var probe = await piHoleApiClient.ProbeAsync(cancellationToken);
            var response = PiHoleDiagnosticsFactory.Create(
                options,
                statusStore.GetSnapshot(),
                cursorStore.GetLastUntilUtc(),
                probe);
            return Ok(ApiResponse<PiHoleDiagnosticsResponse>.SuccessResponse(response));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Pi-hole diagnostics request failed.");
            return BadRequest(ApiResponse<PiHoleDiagnosticsResponse>.ErrorResponse(ex.Message));
        }
    }

    private static PiHoleOptionsDto ToDto(PiHoleOptions options) => new()
    {
        Enabled = options.Enabled,
        BaseUrl = options.BaseUrl,
        AppPassword = string.IsNullOrEmpty(options.AppPassword) ? string.Empty : "********",
        HasAppPassword = !string.IsNullOrEmpty(options.AppPassword),
        PollIntervalSeconds = options.PollIntervalSeconds,
        BatchSize = options.BatchSize,
        LookbackSeconds = options.LookbackSeconds,
        ClientSubnetPrefix = options.ClientSubnetPrefix
    };
}

public sealed class PiHoleOptionsDto
{
    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = string.Empty;

    public string AppPassword { get; set; } = string.Empty;

    public bool HasAppPassword { get; set; }

    public int PollIntervalSeconds { get; set; }

    public int BatchSize { get; set; }

    public int LookbackSeconds { get; set; }

    public string ClientSubnetPrefix { get; set; } = string.Empty;
}
