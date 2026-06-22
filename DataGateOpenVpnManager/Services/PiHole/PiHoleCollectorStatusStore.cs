namespace DataGateOpenVpnManager.Services.PiHole;

public interface IPiHoleCollectorStatusStore
{
    PiHoleCollectorStatusSnapshot GetSnapshot();

    void SetCollectorRunning(bool running);

    void RecordConfigApplied(DateTimeOffset appliedAtUtc);

    void RecordPollSuccess(PiHolePollSuccessResult result);

    void RecordPollFailure(DateTimeOffset atUtc, string error);
}

public sealed class PiHoleCollectorStatusSnapshot
{
    public bool CollectorRunning { get; init; }

    public DateTimeOffset? RuntimeConfigAppliedAtUtc { get; init; }

    public DateTimeOffset? LastPollAtUtc { get; init; }

    public DateTimeOffset? LastSuccessfulPollAtUtc { get; init; }

    public string? LastPollError { get; init; }

    public int LastPollQueriesFetched { get; init; }

    public int LastPollQueriesAfterFilter { get; init; }

    public int LastPollQueriesEnriched { get; init; }

    public int LastPollQueriesForwarded { get; init; }

    public DateTimeOffset? LastCursorUntilUtc { get; init; }
}

public sealed class PiHolePollSuccessResult
{
    public DateTimeOffset AtUtc { get; init; }

    public int QueriesFetched { get; init; }

    public int QueriesAfterFilter { get; init; }

    public int QueriesEnriched { get; init; }

    public int QueriesForwarded { get; init; }

    public DateTimeOffset CursorUntilUtc { get; init; }
}

public sealed class PiHoleCollectorStatusStore : IPiHoleCollectorStatusStore
{
    private readonly object _sync = new();
    private bool _collectorRunning;
    private DateTimeOffset? _runtimeConfigAppliedAtUtc;
    private DateTimeOffset? _lastPollAtUtc;
    private DateTimeOffset? _lastSuccessfulPollAtUtc;
    private string? _lastPollError;
    private int _lastPollQueriesFetched;
    private int _lastPollQueriesAfterFilter;
    private int _lastPollQueriesEnriched;
    private int _lastPollQueriesForwarded;
    private DateTimeOffset? _lastCursorUntilUtc;

    public PiHoleCollectorStatusSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new PiHoleCollectorStatusSnapshot
            {
                CollectorRunning = _collectorRunning,
                RuntimeConfigAppliedAtUtc = _runtimeConfigAppliedAtUtc,
                LastPollAtUtc = _lastPollAtUtc,
                LastSuccessfulPollAtUtc = _lastSuccessfulPollAtUtc,
                LastPollError = _lastPollError,
                LastPollQueriesFetched = _lastPollQueriesFetched,
                LastPollQueriesAfterFilter = _lastPollQueriesAfterFilter,
                LastPollQueriesEnriched = _lastPollQueriesEnriched,
                LastPollQueriesForwarded = _lastPollQueriesForwarded,
                LastCursorUntilUtc = _lastCursorUntilUtc
            };
        }
    }

    public void SetCollectorRunning(bool running)
    {
        lock (_sync)
        {
            _collectorRunning = running;
        }
    }

    public void RecordConfigApplied(DateTimeOffset appliedAtUtc)
    {
        lock (_sync)
        {
            _runtimeConfigAppliedAtUtc = appliedAtUtc.ToUniversalTime();
        }
    }

    public void RecordPollSuccess(PiHolePollSuccessResult result)
    {
        lock (_sync)
        {
            _lastPollAtUtc = result.AtUtc.ToUniversalTime();
            _lastSuccessfulPollAtUtc = result.AtUtc.ToUniversalTime();
            _lastPollError = null;
            _lastPollQueriesFetched = result.QueriesFetched;
            _lastPollQueriesAfterFilter = result.QueriesAfterFilter;
            _lastPollQueriesEnriched = result.QueriesEnriched;
            _lastPollQueriesForwarded = result.QueriesForwarded;
            _lastCursorUntilUtc = result.CursorUntilUtc.ToUniversalTime();
        }
    }

    public void RecordPollFailure(DateTimeOffset atUtc, string error)
    {
        lock (_sync)
        {
            _lastPollAtUtc = atUtc.ToUniversalTime();
            _lastPollError = string.IsNullOrWhiteSpace(error) ? "Unknown Pi-hole poll error." : error.Trim();
        }
    }
}
