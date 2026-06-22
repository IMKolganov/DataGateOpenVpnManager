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
    IPiHoleApiClient piHoleApiClient) : ControllerBase
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
        return Ok(ApiResponse<PiHoleOptionsDto>.SuccessResponse(ToDto(merged)));
    }

    [HttpGet("diagnostics")]
    public async Task<ActionResult<ApiResponse<PiHoleDiagnosticsResponse>>> GetDiagnostics(
        CancellationToken cancellationToken)
    {
        var options = runtimeOptions.GetEffective();
        var probe = await piHoleApiClient.ProbeAsync(cancellationToken);
        return Ok(ApiResponse<PiHoleDiagnosticsResponse>.SuccessResponse(new PiHoleDiagnosticsResponse
        {
            CheckedAtUtc = DateTime.UtcNow,
            Enabled = options.Enabled,
            BaseUrl = options.BaseUrl,
            Authenticated = probe.Authenticated,
            Error = probe.Error,
            SampleQueryCount = probe.SampleQueryCount
        }));
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
