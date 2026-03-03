using DataGateOpenVpnManager.Hubs;

namespace DataGateOpenVpnManager.Tests.Hubs;

public class OpenVpnEventHubTests
{
    [Fact]
    public void Hub_CanBeConstructed_WithTrackerAndLogger()
    {
        var tracker = new HubConnectionTracker();
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<OpenVpnEventHub>();
        var hub = new OpenVpnEventHub(tracker, logger);
        Assert.NotNull(hub);
    }
}
