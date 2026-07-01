using DataGateOpenVpnManager.Configurations;
using DataGateOpenVpnManager.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DataGateOpenVpnManager.Tests.Configurations;

[Collection("EnvironmentVariables")]
public class EasyRsaConfigurationTests
{
    [Fact]
    public void ApplyLegacyEnv_WhenEnvNotSet_UsesDefaults()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var options = new EasyRsaOptions();

        EasyRsaConfiguration.ApplyLegacyEnv(config, options);

        Assert.Equal("index.txt", options.IndexFileName);
        Assert.Equal("ta.key", options.TaKeyFileName);
    }

    [Fact]
    public void ApplyLegacyEnv_WhenEnvSet_OverridesDefaults()
    {
        SetEnv("EASY_RSA_INDEX_FILE", "custom-index.txt");
        SetEnv("EASY_RSA_TA_KEY_FILE", "custom-ta.key");
        try
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
            var options = new EasyRsaOptions();

            EasyRsaConfiguration.ApplyLegacyEnv(config, options);

            Assert.Equal("custom-index.txt", options.IndexFileName);
            Assert.Equal("custom-ta.key", options.TaKeyFileName);
        }
        finally
        {
            SetEnv("EASY_RSA_INDEX_FILE", null);
            SetEnv("EASY_RSA_TA_KEY_FILE", null);
        }
    }

    [Fact]
    public void ApplyLegacyEnv_WhenEasyRsaPathEnvSet_OverridesSectionMainPath()
    {
        SetEnv("EASY_RSA_PATH", "/env/path");
        try
        {
            var configData = new Dictionary<string, string?> { ["EasyRsa:MainPath"] = "/section/path" };
            var config = new ConfigurationBuilder().AddInMemoryCollection(configData!).Build();
            var options = new EasyRsaOptions { MainPath = "/section/path" };

            EasyRsaConfiguration.ApplyLegacyEnv(config, options);

            Assert.Equal("/env/path", options.MainPath);
        }
        finally
        {
            SetEnv("EASY_RSA_PATH", null);
        }
    }

    [Fact]
    public void ConfigureEasyRsa_BindsSectionAndEnvOverrides()
    {
        SetEnv("EASY_RSA_PATH", "/env/path");
        try
        {
            var configData = new Dictionary<string, string?>
            {
                ["EasyRsa:IndexFileName"] = "section-index.txt",
                ["EasyRsa:TaKeyFileName"] = "section-ta.key"
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(configData!).Build();
            var services = new ServiceCollection();
            services.ConfigureEasyRsa(config);
            var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<EasyRsaOptions>>().Value;

            Assert.Equal("/env/path", options.MainPath);
            Assert.Equal("section-index.txt", options.IndexFileName);
            Assert.Equal("section-ta.key", options.TaKeyFileName);
        }
        finally
        {
            SetEnv("EASY_RSA_PATH", null);
        }
    }

    private static void SetEnv(string name, string? value) =>
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
}

[CollectionDefinition("EnvironmentVariables", DisableParallelization = true)]
public class EnvironmentVariablesCollection;
