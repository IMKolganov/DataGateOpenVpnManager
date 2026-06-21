using DataGateOpenVpnManager.Services.Proxy;

namespace DataGateOpenVpnManager.Tests.Services.Proxy;

public class ProxyByteComparisonTests
{
    [Fact]
    public void HasMaterialDelta_WhenAnyDirectionExceedsThreshold()
    {
        var comparison = ProxyByteComparison.Create(10_000, 100, 5_000, 100);
        Assert.True(comparison.HasMaterialDelta(4096));
        Assert.False(comparison.HasMaterialDelta(10_000));
    }
}
