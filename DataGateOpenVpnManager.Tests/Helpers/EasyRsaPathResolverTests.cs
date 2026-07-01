using DataGateOpenVpnManager.Configurations;
using DataGateOpenVpnManager.Helpers;
using DataGateOpenVpnManager.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace DataGateOpenVpnManager.Tests.Helpers;

public class EasyRsaPathResolverTests
{
    [Fact]
    public void GetEasyRsaPath_WhenMainPathSet_ReturnsPath()
    {
        var resolver = new EasyRsaPathResolver(Options.Create(new EasyRsaOptions { MainPath = "/config/path" }));

        var result = resolver.GetEasyRsaPath();

        Assert.Equal("/config/path", result);
    }

    [Fact]
    public void GetEasyRsaPath_WhenMainPathEmpty_ThrowsInvalidOperationException()
    {
        var resolver = new EasyRsaPathResolver(Options.Create(new EasyRsaOptions()));

        var ex = Assert.Throws<InvalidOperationException>(() => resolver.GetEasyRsaPath());

        Assert.Contains("EasyRsa:MainPath", ex.Message);
    }

    [Fact]
    public void ApplyLegacyEnv_WhenEasyRsaPathInConfig_SetsMainPath()
    {
        var configData = new Dictionary<string, string?> { ["EasyRsa:MainPath"] = "/config/path" };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configData!).Build();
        var options = new EasyRsaOptions();
        config.GetSection("EasyRsa").Bind(options);
        EasyRsaConfiguration.ApplyLegacyEnv(config, options);
        var resolver = new EasyRsaPathResolver(Options.Create(options));

        Assert.Equal("/config/path", resolver.GetEasyRsaPath());
    }
}
