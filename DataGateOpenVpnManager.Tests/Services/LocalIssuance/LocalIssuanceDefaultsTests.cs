using DataGateMonitor.SharedModels.DataGateMonitor.VpnServers.Requests;
using DataGateMonitor.SharedModels.Enums;
using DataGateOpenVpnManager.Configurations;
using DataGateOpenVpnManager.Services;
using DataGateOpenVpnManager.Services.Interfaces;
using DataGateOpenVpnManager.Services.LocalIssuance;
using DataGateOpenVpnManager.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DataGateOpenVpnManager.Tests.Services.LocalIssuance;

/// <summary>Without LOCAL_ISSUANCE_ENABLED / CLIENT_CONFIG_DIR an existing node behaves exactly as before.</summary>
public class LocalIssuanceDefaultsTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void FromConfiguration_Defaults_AreOff()
    {
        var options = LocalIssuanceOptions.FromConfiguration(Config(new() { ["DATA_DIR"] = "/openvpn-udp" }));

        Assert.False(options.Enabled);
        Assert.Null(options.ClientConfigDir);
        Assert.False(options.CcdExclusive);
        Assert.Equal("/openvpn-udp/local-issuance/admin.sock", options.SocketPath);
    }

    [Theory]
    [InlineData("true", "/openvpn-udp/ccd")]
    [InlineData("1", "/openvpn-udp/ccd")]
    [InlineData("/srv/ccd/", "/srv/ccd")]
    [InlineData("", null)]
    public void ClientConfigDir_ResolvesLikeServerConfScript(string raw, string? expected)
    {
        Assert.Equal(expected, LocalIssuanceOptions.ResolveClientConfigDir(raw, "/openvpn-udp"));
    }

    [Theory]
    [InlineData(null, false, "255.255.255.0", 0)] // no ccd: server.conf untouched
    [InlineData(null, true, "255.255.255.0", 32)]
    [InlineData("0", true, "255.255.255.0", 0)]
    [InlineData("54", true, "255.255.255.0", 54)]
    [InlineData(null, true, "255.255.255.240", 0)] // /28 pool has 13 addresses: 32 would leave none
    [InlineData("-1", true, "255.255.255.0", 0)]
    [InlineData("abc", true, "255.255.255.0", 0)]
    public void StaticIpReserved_ResolvesLikeServerConfScript(string? raw, bool ccd, string netmask, int expected)
    {
        Assert.Equal(expected, LocalIssuanceOptions.ResolveStaticIpReserved(raw, ccd, "10.50.28.0", netmask));
    }

    [Fact]
    public void Disabled_RegistersNoNodeKeyNoOutboxNoDeliveryWorker()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureLocalIssuance(Config(new() { ["Backend:BaseUrl"] = "http://backend/" }));
        using var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<INodeIdentity>());
        Assert.Null(provider.GetService<ILocalIssuanceOutbox>());
        Assert.DoesNotContain(services, d => d.ImplementationType == typeof(LocalIssuanceEventDeliveryService));
        Assert.False(provider.GetRequiredService<IClientConfigDirStore>().IsEnabled);
    }

    [Fact]
    public void Enabled_RegistersDeliveryWorker()
    {
        using var dir = new TempDir();
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureLocalIssuance(Config(new()
        {
            ["Backend:BaseUrl"] = "http://backend/",
            [LocalIssuanceOptions.EnabledKey] = "true",
            [LocalIssuanceOptions.DirKey] = dir.Combine("li"),
        }));

        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService)
                                       && d.ImplementationType == typeof(LocalIssuanceEventDeliveryService));
    }

    [Fact]
    public void Announce_Disabled_IsSameJsonAsBefore_Enabled_AddsNodeKey()
    {
        var request = new AnnounceVpnServerRequest { ServerType = VpnServerType.OpenVpn, ApiUrl = "http://203.0.113.10:5009/" };

        var disabled = NewAnnounce(new LocalIssuanceOptions(), Mock.Of<IServiceProvider>());
        Assert.Equal(JsonConvert.SerializeObject(request), disabled.BuildAnnounceJson(request));

        var identity = new Mock<INodeIdentity>();
        identity.SetupGet(i => i.PublicKeyPem).Returns("-----BEGIN PUBLIC KEY-----\nAAA\n-----END PUBLIC KEY-----");
        var sp = new Mock<IServiceProvider>();
        sp.Setup(s => s.GetService(typeof(INodeIdentity))).Returns(identity.Object);
        var enabled = NewAnnounce(new LocalIssuanceOptions { Enabled = true }, sp.Object);

        var json = JObject.Parse(enabled.BuildAnnounceJson(request));
        Assert.Equal(identity.Object.PublicKeyPem, json[VpnServerAnnounceHostedService.NodePublicKeyField]!.Value<string>());
        Assert.Equal("http://203.0.113.10:5009/", json["ApiUrl"]!.Value<string>());
    }

    private static VpnServerAnnounceHostedService NewAnnounce(LocalIssuanceOptions options, IServiceProvider services) =>
        new(Mock.Of<IHttpClientFactory>(), Mock.Of<IExternalIpAddressService>(), Config(new()), options, services,
            NullLogger<VpnServerAnnounceHostedService>.Instance);
}
