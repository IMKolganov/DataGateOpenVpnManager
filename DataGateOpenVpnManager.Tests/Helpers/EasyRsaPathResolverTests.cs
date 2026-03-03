using DataGateOpenVpnManager.Helpers;
using Microsoft.Extensions.Configuration;

namespace DataGateOpenVpnManager.Tests.Helpers;

public class EasyRsaPathResolverTests
{
    [Fact]
    public void GetEasyRsaPath_WhenEnvVarSet_ReturnsEnvValue()
    {
        const string expectedPath = "/custom/env/path";
        Environment.SetEnvironmentVariable("EASY_RSA_PATH", expectedPath);
        try
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
            var resolver = new EasyRsaPathResolver(config);

            var result = resolver.GetEasyRsaPath();

            Assert.Equal(expectedPath, result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("EASY_RSA_PATH", null);
        }
    }

    [Fact]
    public void GetEasyRsaPath_WhenEnvVarNull_AndConfigHasEasyRsaMainPath_ReturnsConfigValue()
    {
        Environment.SetEnvironmentVariable("EASY_RSA_PATH", null);
        var configData = new Dictionary<string, string?> { ["EasyRsa:MainPath"] = "/config/path" };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configData!).Build();
        var resolver = new EasyRsaPathResolver(config);

        var result = resolver.GetEasyRsaPath();

        Assert.Equal("/config/path", result);
    }

    [Fact]
    public void GetEasyRsaPath_WhenNeitherEnvNorConfigSet_ThrowsInvalidOperationException()
    {
        Environment.SetEnvironmentVariable("EASY_RSA_PATH", null);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var resolver = new EasyRsaPathResolver(config);

        var ex = Assert.Throws<InvalidOperationException>(() => resolver.GetEasyRsaPath());

        Assert.Contains("EasyRsa:MainPath", ex.Message);
    }
}
