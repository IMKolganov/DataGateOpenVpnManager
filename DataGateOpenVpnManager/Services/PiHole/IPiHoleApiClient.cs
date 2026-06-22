namespace DataGateOpenVpnManager.Services.PiHole;

public interface IPiHoleApiClient
{
    Task<IReadOnlyList<PiHoleQueryRecord>> GetQueriesSinceAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset untilUtc,
        int maxCount,
        CancellationToken cancellationToken);

    Task<(bool Authenticated, int SampleQueryCount, string? Error)> ProbeAsync(CancellationToken cancellationToken);
}
