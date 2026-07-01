using DataGateOpenVpnManager.Services.PiHole;

namespace DataGateOpenVpnManager.Tests.Services.PiHole;

public class PiHoleCollectorStatusStoreTests
{
    [Fact]
    public void RecordPollSuccess_ThenFailure_UpdatesSnapshot()
    {
        var store = new PiHoleCollectorStatusStore();
        store.SetCollectorRunning(true);
        store.RecordConfigApplied(DateTimeOffset.UtcNow.AddMinutes(-5));

        store.RecordPollSuccess(new PiHolePollSuccessResult
        {
            AtUtc = DateTimeOffset.UtcNow,
            QueriesFetched = 10,
            QueriesAfterFilter = 4,
            QueriesEnriched = 2,
            QueriesForwarded = 2,
            CursorUntilUtc = DateTimeOffset.UtcNow
        });

        var ok = store.GetSnapshot();
        Assert.True(ok.CollectorRunning);
        Assert.Equal(2, ok.LastPollQueriesForwarded);
        Assert.Null(ok.LastPollError);

        store.RecordPollFailure(DateTimeOffset.UtcNow, "timeout");
        var failed = store.GetSnapshot();
        Assert.Equal("timeout", failed.LastPollError);
        Assert.Equal(2, failed.LastPollQueriesForwarded);
    }
}
