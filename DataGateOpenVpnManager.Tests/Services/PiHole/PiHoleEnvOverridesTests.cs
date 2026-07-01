using DataGateOpenVpnManager.Models;
using DataGateOpenVpnManager.Services.PiHole;
using Microsoft.Extensions.Configuration;

namespace DataGateOpenVpnManager.Tests.Services.PiHole;

public class PiHoleEnvOverridesTests
{
    [Fact]
    public void FromConfiguration_ReadsLegacyPiholeEnvKeys()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PIHOLE_ENABLED"] = "true",
                ["PIHOLE_BASE_URL"] = "http://legacy-env",
                ["PIHOLE_APP_PASSWORD"] = "legacy-secret",
                ["PIHOLE_POLL_INTERVAL_SEC"] = "45",
                ["PIHOLE_BATCH_SIZE"] = "300",
                ["PIHOLE_LOOKBACK_SEC"] = "90",
                ["PIHOLE_CLIENT_SUBNET_PREFIX"] = "10.51.30."
            })
            .Build();

        var overrides = PiHoleEnvOverrides.FromConfiguration(config);

        Assert.True(overrides.HasAny);
        Assert.True(overrides.Enabled);
        Assert.Equal("http://legacy-env", overrides.BaseUrl);
        Assert.Equal("legacy-secret", overrides.AppPassword);
        Assert.Equal(45, overrides.PollIntervalSeconds);
        Assert.Equal(300, overrides.BatchSize);
        Assert.Equal(90, overrides.LookbackSeconds);
        Assert.Equal("10.51.30.", overrides.ClientSubnetPrefix);
    }

    [Fact]
    public void ApplyTo_OnlyOverridesSetFields()
    {
        var options = new PiHoleOptions
        {
            Enabled = true,
            BaseUrl = "http://disk",
            AppPassword = "disk-secret",
            PollIntervalSeconds = 60,
            BatchSize = 200,
            LookbackSeconds = 120,
            ClientSubnetPrefix = "10.51.16."
        };

        new PiHoleEnvOverrides { BaseUrl = "http://from-env", Enabled = false }.ApplyTo(options);

        Assert.False(options.Enabled);
        Assert.Equal("http://from-env", options.BaseUrl);
        Assert.Equal("disk-secret", options.AppPassword);
        Assert.Equal(60, options.PollIntervalSeconds);
        Assert.Equal("10.51.16.", options.ClientSubnetPrefix);
    }

    [Fact]
    public void FromConfiguration_ReturnsEmptyWhenNoEnvSet()
    {
        var config = new ConfigurationBuilder().Build();

        var overrides = PiHoleEnvOverrides.FromConfiguration(config);

        Assert.False(overrides.HasAny);
    }

    [Fact]
    public void FromConfiguration_ReadsStandardPiHoleDoubleUnderscoreEnv()
    {
        Environment.SetEnvironmentVariable("PiHole__Enabled", "true");
        Environment.SetEnvironmentVariable("PiHole__BaseUrl", "http://standard-env");
        try
        {
            var config = new ConfigurationBuilder().Build();
            var overrides = PiHoleEnvOverrides.FromConfiguration(config);

            Assert.True(overrides.Enabled);
            Assert.Equal("http://standard-env", overrides.BaseUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PiHole__Enabled", null);
            Environment.SetEnvironmentVariable("PiHole__BaseUrl", null);
        }
    }
}
