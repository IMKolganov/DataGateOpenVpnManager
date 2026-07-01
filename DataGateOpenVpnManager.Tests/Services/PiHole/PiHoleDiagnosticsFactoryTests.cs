using DataGateOpenVpnManager.Models;
using DataGateOpenVpnManager.Services.PiHole;

namespace DataGateOpenVpnManager.Tests.Services.PiHole;

public class PiHoleDiagnosticsFactoryTests
{
    [Fact]
    public void Create_MapsOptionsStatusProbeAndCursor()
    {
        var appliedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var pollAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var cursor = DateTimeOffset.UtcNow;

        var response = PiHoleDiagnosticsFactory.Create(
            new PiHoleOptions
            {
                Enabled = true,
                BaseUrl = "http://pi-hole:8080",
                AppPassword = "secret",
                PollIntervalSeconds = 45,
                BatchSize = 250,
                LookbackSeconds = 90,
                ClientSubnetPrefix = "10.51.30."
            },
            new PiHoleCollectorStatusSnapshot
            {
                CollectorRunning = true,
                RuntimeConfigAppliedAtUtc = appliedAt,
                LastPollAtUtc = pollAt,
                LastSuccessfulPollAtUtc = pollAt,
                LastPollQueriesFetched = 10,
                LastPollQueriesAfterFilter = 4,
                LastPollQueriesEnriched = 2,
                LastPollQueriesForwarded = 2,
                LastCursorUntilUtc = cursor
            },
            cursor,
            (Authenticated: true, SampleQueryCount: 3, Error: null));

        Assert.True(response.Enabled);
        Assert.Equal("http://pi-hole:8080", response.BaseUrl);
        Assert.True(response.HasAppPassword);
        Assert.Equal(45, response.PollIntervalSeconds);
        Assert.Equal(250, response.BatchSize);
        Assert.Equal(90, response.LookbackSeconds);
        Assert.Equal("10.51.30.", response.ClientSubnetPrefix);
        Assert.True(response.Authenticated);
        Assert.Equal(3, response.SampleQueryCount);
        Assert.True(response.CollectorRunning);
        Assert.Equal(10, response.LastPollQueriesFetched);
        Assert.Equal(4, response.LastPollQueriesAfterFilter);
        Assert.Equal(2, response.LastPollQueriesEnriched);
        Assert.Equal(2, response.LastPollQueriesForwarded);
        Assert.Equal(cursor.UtcDateTime, response.LastCursorUntilUtc);
    }

    [Fact]
    public void Create_UsesProbeErrorAndEmptyPasswordFlag()
    {
        var response = PiHoleDiagnosticsFactory.Create(
            new PiHoleOptions { Enabled = true, BaseUrl = "http://pi-hole:8080" },
            new PiHoleCollectorStatusSnapshot(),
            null,
            (Authenticated: false, SampleQueryCount: 0, Error: "Connection refused"));

        Assert.False(response.HasAppPassword);
        Assert.False(response.Authenticated);
        Assert.Equal("Connection refused", response.Error);
    }

    [Fact]
    public void Create_UsesPersistedAppliedAtWhenInMemoryStatusMissing()
    {
        var persistedAppliedAt = DateTimeOffset.UtcNow.AddHours(-2);

        var response = PiHoleDiagnosticsFactory.Create(
            new PiHoleOptions { Enabled = true, BaseUrl = "http://pi-hole:8080", AppPassword = "secret" },
            new PiHoleCollectorStatusSnapshot { CollectorRunning = true },
            null,
            (Authenticated: true, SampleQueryCount: 1, Error: null),
            persistedAppliedAt);

        Assert.Equal(persistedAppliedAt.UtcDateTime, response.RuntimeConfigAppliedAtUtc);
    }
}
