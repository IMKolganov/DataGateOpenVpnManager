using DataGateOpenVpnManager.Helpers;
using DataGateOpenVpnManager.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Moq;

namespace DataGateOpenVpnManager.Tests.Helpers;

public class VpnServerAnnounceApiUrlResolverTests
{
    [Fact]
    public void Resolve_PublicApiUrl_WinsOverPublicIpAndPort()
    {
        var result = VpnServerAnnounceApiUrlResolver.Resolve(
            "https://vpn.example.com:9443",
            "203.0.113.10",
            5010);

        Assert.Equal("https://vpn.example.com:9443/", result);
    }

    [Fact]
    public void Resolve_PublicApiUrl_PreservesTrailingSlash()
    {
        var result = VpnServerAnnounceApiUrlResolver.Resolve(
            "https://vpn.example.com/",
            "203.0.113.10",
            5010);

        Assert.Equal("https://vpn.example.com/", result);
    }

    [Fact]
    public void Resolve_WithoutPublicApiUrl_BuildsFromIpAndPort()
    {
        var result = VpnServerAnnounceApiUrlResolver.Resolve(null, "203.0.113.10", 5011);

        Assert.Equal("http://203.0.113.10:5011/", result);
    }

    [Fact]
    public void Resolve_WhitespacePublicApiUrl_FallsBackToIp()
    {
        var result = VpnServerAnnounceApiUrlResolver.Resolve("   ", "198.51.100.2", 5010);

        Assert.Equal("http://198.51.100.2:5010/", result);
    }

    [Fact]
    public void Resolve_MissingIp_ReturnsNull()
    {
        Assert.Null(VpnServerAnnounceApiUrlResolver.Resolve(null, null, 5010));
        Assert.Null(VpnServerAnnounceApiUrlResolver.Resolve(null, "  ", 5010));
    }

    [Fact]
    public void ResolveApiPort_UsesConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["API_PORT"] = "5012" })
            .Build();

        Assert.Equal(5012, VpnServerAnnounceApiUrlResolver.ResolveApiPort(config));
    }

    [Fact]
    public void ResolveApiPort_DefaultsWhenUnset()
    {
        var config = new ConfigurationBuilder().Build();
        Assert.Equal(VpnServerAnnounceApiUrlResolver.DefaultApiPort, VpnServerAnnounceApiUrlResolver.ResolveApiPort(config));
    }

    [Fact]
    public void GetConfiguredPublicApiUrl_UsesConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PUBLIC_API_URL"] = "https://from-config.example/"
            })
            .Build();

        Assert.Equal(
            "https://from-config.example/",
            VpnServerAnnounceApiUrlResolver.GetConfiguredPublicApiUrl(config));
    }

    [Fact]
    public void GetConfiguredPublicIp_UsesConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PUBLIC_IP"] = "81.27.109.193" })
            .Build();

        Assert.Equal("81.27.109.193", VpnServerAnnounceApiUrlResolver.GetConfiguredPublicIp(config));
    }

    [Fact]
    public async Task ResolvePublicIpForAnnounceAsync_UsesConfiguredIpWithoutExternalLookup()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PUBLIC_IP"] = "81.27.109.193" })
            .Build();
        var external = new Mock<IExternalIpAddressService>();

        var ip = await VpnServerAnnounceApiUrlResolver.ResolvePublicIpForAnnounceAsync(
            config, external.Object, CancellationToken.None);

        Assert.Equal("81.27.109.193", ip);
        external.Verify(
            x => x.GetPublicIpAddressAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolvePublicIpForAnnounceAsync_UsesIpLiteralFromPublicApiUrl_WithoutDnsOrExternal()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PUBLIC_API_URL"] = "https://203.0.113.50/"
            })
            .Build();
        var external = new Mock<IExternalIpAddressService>();

        var ip = await VpnServerAnnounceApiUrlResolver.ResolvePublicIpForAnnounceAsync(
            config, external.Object, CancellationToken.None);

        Assert.Equal("203.0.113.50", ip);
        external.Verify(
            x => x.GetPublicIpAddressAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolvePublicIpForAnnounceAsync_FallsBackToExternal_WhenNoPublicIpOrApiUrl()
    {
        var config = new ConfigurationBuilder().Build();
        var external = new Mock<IExternalIpAddressService>();
        external
            .Setup(x => x.GetPublicIpAddressAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("198.51.100.7");

        var ip = await VpnServerAnnounceApiUrlResolver.ResolvePublicIpForAnnounceAsync(
            config, external.Object, CancellationToken.None);

        Assert.Equal("198.51.100.7", ip);
        external.Verify(
            x => x.GetPublicIpAddressAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-ip")]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("::1")]
    [InlineData("2001:db8::1")]
    public void TryParseConfiguredPublicIp_RejectsInvalid(string? raw)
    {
        Assert.False(VpnServerAnnounceApiUrlResolver.TryParseConfiguredPublicIp(raw, out _));
    }

    [Theory]
    [InlineData("81.27.109.193", "81.27.109.193")]
    [InlineData("  203.0.113.10 \n", "203.0.113.10")]
    public void TryParseConfiguredPublicIp_AcceptsPublicIpv4(string raw, string expected)
    {
        Assert.True(VpnServerAnnounceApiUrlResolver.TryParseConfiguredPublicIp(raw, out var ip));
        Assert.Equal(expected, ip);
    }
}
