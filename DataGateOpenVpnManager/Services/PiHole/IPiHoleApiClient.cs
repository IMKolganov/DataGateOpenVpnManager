namespace DataGateOpenVpnManager.Services.PiHole;

public sealed class PiHoleQueryFetchResult
{
    public IReadOnlyList<PiHoleQueryRecord> Records { get; init; } = Array.Empty<PiHoleQueryRecord>();

    public int TotalFromApi { get; init; }
}

public interface IPiHoleApiClient
{
    Task<PiHoleQueryFetchResult> GetQueriesSinceAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset untilUtc,
        int maxCount,
        CancellationToken cancellationToken);

    Task<(bool Authenticated, int SampleQueryCount, string? Error)> ProbeAsync(CancellationToken cancellationToken);
}
