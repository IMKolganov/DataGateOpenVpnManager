using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Models;

namespace DataGateOpenVpnManager.Tests.Models;

public class OpenVpnManagementOptionsTests
{
    [Fact]
    public void DefaultValues_AreSet()
    {
        var options = new OpenVpnManagementOptions();
        Assert.Equal("localhost", options.Host);
        Assert.Equal(5093, options.Port);
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        var options = new OpenVpnManagementOptions
        {
            Host = "192.168.1.1",
            Port = 5095
        };
        Assert.Equal("192.168.1.1", options.Host);
        Assert.Equal(5095, options.Port);
    }
}
