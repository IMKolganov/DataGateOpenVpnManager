using DataGateOpenVpnManager.Hubs;

namespace DataGateOpenVpnManager.Tests.Hubs;

public class HubConnectionTrackerTests
{
    [Fact]
    public void EventHubConnected_AddsConnection_CountIncrements()
    {
        var tracker = new HubConnectionTracker();
        Assert.Equal(0, tracker.EventHubCount);

        tracker.EventHubConnected("conn-1");
        Assert.Equal(1, tracker.EventHubCount);

        tracker.EventHubConnected("conn-2");
        Assert.Equal(2, tracker.EventHubCount);
    }

    [Fact]
    public void EventHubDisconnected_RemovesConnection_CountDecrements()
    {
        var tracker = new HubConnectionTracker();
        tracker.EventHubConnected("conn-1");
        tracker.EventHubConnected("conn-2");
        Assert.Equal(2, tracker.EventHubCount);

        tracker.EventHubDisconnected("conn-1");
        Assert.Equal(1, tracker.EventHubCount);

        tracker.EventHubDisconnected("conn-2");
        Assert.Equal(0, tracker.EventHubCount);
    }

    [Fact]
    public void EventHubDisconnected_WhenIdNotPresent_DoesNotThrow()
    {
        var tracker = new HubConnectionTracker();
        tracker.EventHubDisconnected("nonexistent");
        Assert.Equal(0, tracker.EventHubCount);
    }

    [Fact]
    public void SignalHubConnected_AndDisconnected_UpdatesCount()
    {
        var tracker = new HubConnectionTracker();
        Assert.Equal(0, tracker.SignalHubCount);

        tracker.SignalHubConnected("sig-1");
        tracker.SignalHubConnected("sig-2");
        Assert.Equal(2, tracker.SignalHubCount);

        tracker.SignalHubDisconnected("sig-1");
        Assert.Equal(1, tracker.SignalHubCount);
    }

    [Fact]
    public void TouchHeartbeat_UpdatesLastHeartbeatUtc()
    {
        var tracker = new HubConnectionTracker();
        var before = DateTimeOffset.UtcNow;
        tracker.TouchHeartbeat();
        var after = DateTimeOffset.UtcNow;

        Assert.True(tracker.LastHeartbeatUtc >= before.AddSeconds(-1));
        Assert.True(tracker.LastHeartbeatUtc <= after.AddSeconds(1));
    }
}
