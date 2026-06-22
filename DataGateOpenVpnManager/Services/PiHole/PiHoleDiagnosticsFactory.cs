using DataGateOpenVpnManager.Models;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Diagnostics.Responses;

namespace DataGateOpenVpnManager.Services.PiHole;

public static class PiHoleDiagnosticsFactory
{
    public static PiHoleDiagnosticsResponse Create(
        PiHoleOptions options,
        PiHoleCollectorStatusSnapshot status,
        DateTimeOffset? cursorUntilUtc,
        (bool Authenticated, int SampleQueryCount, string? Error) probe)
    {
        var checkedAt = DateTime.UtcNow;
        var hasPassword = !string.IsNullOrEmpty(options.AppPassword);

        return new PiHoleDiagnosticsResponse
        {
            CheckedAtUtc = checkedAt,
            Enabled = options.Enabled,
            BaseUrl = options.BaseUrl,
            HasAppPassword = hasPassword,
            PollIntervalSeconds = options.PollIntervalSeconds,
            BatchSize = options.BatchSize,
            LookbackSeconds = options.LookbackSeconds,
            ClientSubnetPrefix = options.ClientSubnetPrefix,
            Authenticated = probe.Authenticated,
            Error = probe.Error,
            SampleQueryCount = probe.SampleQueryCount,
            CollectorRunning = status.CollectorRunning,
            RuntimeConfigAppliedAtUtc = status.RuntimeConfigAppliedAtUtc?.UtcDateTime,
            LastPollAtUtc = status.LastPollAtUtc?.UtcDateTime,
            LastSuccessfulPollAtUtc = status.LastSuccessfulPollAtUtc?.UtcDateTime,
            LastPollError = status.LastPollError,
            LastPollQueriesFetched = status.LastPollQueriesFetched,
            LastPollQueriesAfterFilter = status.LastPollQueriesAfterFilter,
            LastPollQueriesEnriched = status.LastPollQueriesEnriched,
            LastPollQueriesForwarded = status.LastPollQueriesForwarded,
            LastCursorUntilUtc = cursorUntilUtc?.UtcDateTime ?? status.LastCursorUntilUtc?.UtcDateTime
        };
    }
}
