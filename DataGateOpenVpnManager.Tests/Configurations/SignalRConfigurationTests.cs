using DataGateOpenVpnManager.Configurations;
using DataGateOpenVpnManager.Hubs;
using DataGateOpenVpnManager.Models;
using DataGateOpenVpnManager.Services.OpenVpnTelnet;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DataGateOpenVpnManager.Tests.Configurations;

public class SignalRConfigurationTests
{
    [Fact]
    public void ConfigureSignalR_RegistersOpenVpnManagementOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configData = new Dictionary<string, string?>
        {
            ["OpenVpnManagement:Host"] = "127.0.0.1",
            ["OpenVpnManagement:Port"] = "5095"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configData!).Build();

        services.ConfigureSignalR(config);
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpenVpnManagementOptions>>().Value;
        Assert.Equal("127.0.0.1", options.Host);
        Assert.Equal(5095, options.Port);
    }

    [Fact]
    public void ConfigureSignalR_RegistersTelnetClientAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configData = new Dictionary<string, string?>
        {
            ["OpenVpnManagement:Host"] = "localhost",
            ["OpenVpnManagement:Port"] = "5092"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configData!).Build();

        services.ConfigureSignalR(config);
        var provider = services.BuildServiceProvider();

        var client1 = provider.GetRequiredService<TelnetClient>();
        var client2 = provider.GetRequiredService<TelnetClient>();
        Assert.Same(client1, client2);
    }

    [Fact]
    public void ConfigureSignalR_RegistersHubConnectionTracker()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OpenVpnManagement:Host"] = "x", ["OpenVpnManagement:Port"] = "1" }!)
            .Build();

        services.ConfigureSignalR(config);
        var provider = services.BuildServiceProvider();

        var tracker = provider.GetRequiredService<HubConnectionTracker>();
        Assert.NotNull(tracker);
        Assert.Equal(0, tracker.EventHubCount);
        Assert.Equal(0, tracker.SignalHubCount);
    }

    [Fact]
    public void ConfigureSignalR_RegistersOpenVpnManagementSignalServiceAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OpenVpnManagement:Host"] = "x", ["OpenVpnManagement:Port"] = "1" }!)
            .Build();

        services.ConfigureSignalR(config);
        var provider = services.BuildServiceProvider();

        var svc1 = provider.GetRequiredService<OpenVpnManagementSignalService>();
        var svc2 = provider.GetRequiredService<OpenVpnManagementSignalService>();
        Assert.Same(svc1, svc2);
    }
}
