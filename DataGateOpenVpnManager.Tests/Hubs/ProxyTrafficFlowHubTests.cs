using DataGateOpenVpnManager.Hubs;
using Microsoft.Extensions.Logging.Abstractions;

namespace DataGateOpenVpnManager.Tests.Hubs;

public class ProxyTrafficFlowHubTests
{
    [Fact]
    public void Hub_CanBeConstructed_WithLogger()
    {
        var hub = new ProxyTrafficFlowHub(new NullLogger<ProxyTrafficFlowHub>());
        Assert.NotNull(hub);
    }
}
