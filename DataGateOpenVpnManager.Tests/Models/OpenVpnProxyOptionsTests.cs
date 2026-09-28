using DataGateOpenVpnManager.Configurations;
using DataGateOpenVpnManager.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DataGateOpenVpnManager.Tests.Models;

public class OpenVpnProxyOptionsTests
{
    [Fact]
    public void Bind_FromOpenVpnProxySection_MapsExpectedDefaults()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenVpnProxy:SessionAuditBufferSize"] = "1000",
                ["OpenVpnProxy:ByteDebugWarnDeltaBytes"] = "8192",
                ["OpenVpnProxy:ZombieMinConsecutiveMisses"] = "5",
                ["OpenVpnProxy:ZombieCheckIntervalSeconds"] = "15",
                ["OpenVpnProxy:ManagementStatusRefreshSeconds"] = "20",
                ["OpenVpnProxy:TlsLogEnrichmentEnabled"] = "false"
            })
            .Build();

        var options = config.GetSection("OpenVpnProxy").Get<OpenVpnProxyOptions>();
        Assert.NotNull(options);
        Assert.Equal(1000, options!.SessionAuditBufferSize);
        Assert.Equal(8192, options.ByteDebugWarnDeltaBytes);
        Assert.Equal(5, options.ZombieMinConsecutiveMisses);
        Assert.Equal(15, options.ZombieCheckIntervalSeconds);
        Assert.Equal(20, options.ManagementStatusRefreshSeconds);
        Assert.False(options.TlsLogEnrichmentEnabled);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("yes", true)]
    [InlineData("on", true)]
    [InlineData("0", false)]
    public void ConfigureProxy_LegacyProxyByteDebugEnv_IsApplied(string legacyValue, bool expected)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PROXY_BYTE_DEBUG"] = legacyValue
            })
            .Build();

        var services = new ServiceCollection();
        services.ConfigureProxy(config);
        var options = services.BuildServiceProvider().GetRequiredService<Microsoft.Extensions.Options.IOptions<OpenVpnProxyOptions>>().Value;

        Assert.Equal(expected, options.ByteDebug);
    }

    [Fact]
    public void NeedsBackgroundManagementRefresh_WhenZombieEnabled_IsTrue()
    {
        var options = new OpenVpnProxyOptions { CloseZombieAfterMissingSeconds = 60 };
        Assert.True(options.NeedsBackgroundManagementRefresh(piHoleCollectorEnabled: false));
    }

    [Fact]
    public void ManagementCacheMaxAge_ScalesWithRefreshInterval()
    {
        var options = new OpenVpnProxyOptions { ManagementStatusRefreshSeconds = 30 };
        Assert.Equal(TimeSpan.FromSeconds(60), options.ManagementCacheMaxAge);
    }
}
