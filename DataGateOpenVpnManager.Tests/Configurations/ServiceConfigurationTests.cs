using DataGateOpenVpnManager.Configurations;
using DataGateOpenVpnManager.Helpers;
using DataGateOpenVpnManager.Services.EasyRsaServices.Interfaces;
using DataGateOpenVpnManager.Services.Interfaces;
using DataGateOpenVpnManager.Services.Proxy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DataGateOpenVpnManager.Tests.Configurations;

public class ServiceConfigurationTests
{
    [Fact]
    public void ConfigureServices_RegistersScopedServices()
    {
        var services = new ServiceCollection();
        var configData = new Dictionary<string, string?> { ["Backend:BaseUrl"] = "http://localhost:9999/" };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configData!).Build();
        services.AddSingleton<IConfiguration>(config);

        services.ConfigureServices(config);
        var provider = services.BuildServiceProvider();

        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var ovpn1 = scope1.ServiceProvider.GetService<IOvpnFileService>();
        var ovpn2 = scope2.ServiceProvider.GetService<IOvpnFileService>();
        Assert.NotNull(ovpn1);
        Assert.NotNull(ovpn2);
        Assert.NotSame(ovpn1, ovpn2);

        var easyRsa1 = scope1.ServiceProvider.GetService<IEasyRsaService>();
        var easyRsa2 = scope2.ServiceProvider.GetService<IEasyRsaService>();
        Assert.NotNull(easyRsa1);
        Assert.NotSame(easyRsa1, easyRsa2);

        var openVpnServer = scope1.ServiceProvider.GetService<IOpenVpnServerService>();
        Assert.NotNull(openVpnServer);
    }

    [Fact]
    public void ConfigureServices_RegistersSingletonEasyRsaPathResolver()
    {
        var services = new ServiceCollection();
        var configData = new Dictionary<string, string?> { ["Backend:BaseUrl"] = "http://localhost:9999/" };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configData!).Build();
        services.AddSingleton<IConfiguration>(config);

        services.ConfigureServices(config);
        var provider = services.BuildServiceProvider();

        var resolver1 = provider.GetService<IEasyRsaPathResolver>();
        var resolver2 = provider.GetService<IEasyRsaPathResolver>();
        Assert.NotNull(resolver1);
        Assert.Same(resolver1, resolver2);
    }

    [Fact]
    public void ConfigureServices_RegistersMicroserviceJwtValidator()
    {
        var services = new ServiceCollection();
        var configData = new Dictionary<string, string?> { ["Backend:BaseUrl"] = "http://localhost:9999/" };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configData!).Build();
        services.AddSingleton<IConfiguration>(config);

        services.ConfigureServices(config);
        var provider = services.BuildServiceProvider();

        var validator = provider.GetRequiredService<IMicroserviceJwtValidator>();
        Assert.NotNull(validator);
    }

    [Fact]
    public void ConfigureServices_RegistersProxyTrackingSingletons()
    {
        var services = new ServiceCollection();
        var configData = new Dictionary<string, string?> { ["Backend:BaseUrl"] = "http://localhost:9999/" };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configData!).Build();
        services.AddSingleton<IConfiguration>(config);

        services.ConfigureServices(config);
        var provider = services.BuildServiceProvider();

        var active1 = provider.GetRequiredService<IActiveProxyConnectionService>();
        var active2 = provider.GetRequiredService<IActiveProxyConnectionService>();
        Assert.Same(active1, active2);

        var history1 = provider.GetRequiredService<IProxyConnectionHistoryService>();
        var history2 = provider.GetRequiredService<IProxyConnectionHistoryService>();
        Assert.Same(history1, history2);
    }
}
