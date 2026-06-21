using DataGateOpenVpnManager.Services.OpenVpnEvent;

namespace DataGateOpenVpnManager.Tests.Services.OpenVpnEvent;

public class OpenVpnEventServiceTests
{
    [Fact]
    public void Service_CanBeConstructed()
    {
        Assert.NotNull(new OpenVpnEventService());
    }
}
