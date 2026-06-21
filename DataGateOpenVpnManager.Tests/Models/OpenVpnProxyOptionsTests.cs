using DataGateOpenVpnManager.Models;

namespace DataGateOpenVpnManager.Tests.Models;

public class OpenVpnProxyOptionsTests
{
    [Fact]
    public void IsSessionAuditEnabled_WhenByteDebugOrSessionAudit()
    {
        Assert.True(new OpenVpnProxyOptions { ByteDebug = true }.IsSessionAuditEnabled);
        Assert.True(new OpenVpnProxyOptions { SessionAudit = true }.IsSessionAuditEnabled);
        Assert.False(new OpenVpnProxyOptions().IsSessionAuditEnabled);
    }

    [Theory]
    [InlineData(true, 10, true)]
    [InlineData(false, 10, true)]
    [InlineData(true, 0, false)]
    [InlineData(false, 0, false)]
    public void NeedsBackgroundManagementRefresh_ReflectsZombieAndPeriodicByteDebug(
        bool byteDebug,
        int byteDebugInterval,
        bool zombieEnabled)
    {
        var options = new OpenVpnProxyOptions
        {
            ByteDebug = byteDebug,
            ByteDebugIntervalSeconds = byteDebugInterval,
            CloseZombieAfterMissingSeconds = zombieEnabled ? 60 : 0
        };

        Assert.Equal(zombieEnabled || byteDebug && byteDebugInterval > 0, options.NeedsBackgroundManagementRefresh);
    }
}
