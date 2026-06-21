using DataGateOpenVpnManager.Configurations;
using DataGateOpenVpnManager.Models;
using DataGateOpenVpnManager.Services.Proxy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DataGateOpenVpnManager.Tests.Configurations;

public class ProxyConfigurationTests
{
    [Fact]
    public void ConfigureProxy_AppliesLegacyProxyByteDebugEnv()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PROXY_BYTE_DEBUG"] = "1" })
            .Build();

        services.ConfigureProxy(config);
        var options = services.BuildServiceProvider().GetRequiredService<IOptions<OpenVpnProxyOptions>>().Value;

        Assert.True(options.ByteDebug);
    }

    [Fact]
    public void ConfigureProxy_RegistersProxyServices()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        services.AddLogging();
        services.ConfigureProxy(config);
        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IProxySessionAuditService>());
        Assert.NotNull(provider.GetService<IProxyConnectionLifetimeService>());
    }
}
