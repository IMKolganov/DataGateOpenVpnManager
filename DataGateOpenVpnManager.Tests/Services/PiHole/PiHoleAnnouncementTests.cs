using DataGateMonitor.SharedModels.DataGateMonitor.VpnServers.Requests;
using DataGateOpenVpnManager.Configurations;
using DataGateOpenVpnManager.Models;
using DataGateOpenVpnManager.Services;
using DataGateOpenVpnManager.Services.Interfaces;
using DataGateOpenVpnManager.Services.PiHole;
using DataGateOpenVpnManager.Services.LocalIssuance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json.Linq;

namespace DataGateOpenVpnManager.Tests.Services.PiHole;

public class PiHoleAnnouncementTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Announcement_ReportsConfiguredPiHole_EvenBeforeCollectorIsEnabled(bool enabled)
    {
        var json = BuildAnnouncement("https://backend.example.com/", new PiHoleOptions
        {
            Enabled = enabled, BaseUrl = "http://10.51.40.1:8080",
            AppPassword = "synthetic-credential", ClientSubnetPrefix = "10.51.40."
        });

        Assert.True(json["HasPiHole"]!.Value<bool>());
        Assert.Equal("synthetic-credential", json["PiHole"]!["AppPassword"]!.Value<string>());
        Assert.Equal("10.51.40.", json["PiHole"]!["ClientSubnetPrefix"]!.Value<string>());
    }

    [Fact]
    public void Announcement_DoesNotClaimPiHole_WhenPasswordIsMissing()
    {
        var json = BuildAnnouncement("https://backend.example.com/", new PiHoleOptions());
        Assert.False(json["HasPiHole"]!.Value<bool>());
        Assert.Equal(JTokenType.Null, json["PiHole"]!.Type);
    }

    [Theory]
    [InlineData("http://backend.example.com/", false)]
    [InlineData("https://backend.example.com/", true)]
    [InlineData("http://127.0.0.1:5581/", true)]
    public void Announcement_SendsCredentialsOnlyOverHttpsOrLoopback(string backendUrl, bool expected)
    {
        var json = BuildAnnouncement(backendUrl, new PiHoleOptions { AppPassword = "synthetic-credential" });
        Assert.Equal(expected, json["PiHole"]!.Type != JTokenType.Null);
        if (!expected)
            Assert.DoesNotContain("synthetic-credential", json.ToString());
    }

    [Fact]
    public void BootstrapSettings_AreAnnounceable_AndCanBeReplacedByDashboardRuntime()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pihole-bootstrap-" + Guid.NewGuid().ToString("N"));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DATA_DIR"] = directory,
            ["PIHOLE_BOOTSTRAP_BASE_URL"] = "http://10.51.40.1:8080",
            ["PIHOLE_BOOTSTRAP_APP_PASSWORD"] = "initial-credential",
            ["PIHOLE_BOOTSTRAP_CLIENT_SUBNET_PREFIX"] = "10.51.40."
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.ConfigurePiHole(config);
        try
        {
            using var provider = services.BuildServiceProvider();
            var store = provider.GetRequiredService<IPiHoleRuntimeOptionsStore>();
            Assert.False(store.GetEffective().Enabled);
            Assert.Equal("initial-credential", store.GetEffective().AppPassword);
            Assert.False(store.HasEnvOverrides);

            store.Apply(new PiHoleOptions
            {
                Enabled = true, BaseUrl = "http://replacement:8080", AppPassword = "replacement-credential",
                ClientSubnetPrefix = "10.51.42."
            });
            Assert.True(store.GetEffective().Enabled);
            Assert.Equal("http://replacement:8080", store.GetEffective().BaseUrl);
            Assert.Equal("replacement-credential", store.GetEffective().AppPassword);
            Assert.Equal("10.51.42.", store.GetEffective().ClientSubnetPrefix);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static JObject BuildAnnouncement(string backendUrl, PiHoleOptions options)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Backend:BaseUrl"] = backendUrl
        }).Build();
        var store = new Mock<IPiHoleRuntimeOptionsStore>();
        store.Setup(s => s.GetEffective()).Returns(options);
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(IPiHoleRuntimeOptionsStore))).Returns(store.Object);
        var service = new VpnServerAnnounceHostedService(Mock.Of<IHttpClientFactory>(),
            Mock.Of<IExternalIpAddressService>(), config, new LocalIssuanceOptions(), services.Object,
            NullLogger<VpnServerAnnounceHostedService>.Instance);
        return JObject.Parse(service.BuildAnnounceJson(new AnnounceVpnServerRequest { ApiUrl = "https://node.example.com/" }));
    }
}
