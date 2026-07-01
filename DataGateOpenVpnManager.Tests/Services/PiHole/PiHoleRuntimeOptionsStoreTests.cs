using DataGateOpenVpnManager.Models;
using DataGateOpenVpnManager.Services.PiHole;

namespace DataGateOpenVpnManager.Tests.Services.PiHole;

public class PiHoleRuntimeOptionsStoreTests
{
    [Fact]
    public void GetEffective_ReturnsOverrideAfterApply()
    {
        var sut = PiHoleRuntimeOptionsStoreTestHelper.Create(new PiHoleOptions
        {
            Enabled = false,
            BaseUrl = "http://env-default",
            PollIntervalSeconds = 30
        });

        sut.Apply(new PiHoleOptions
        {
            Enabled = true,
            BaseUrl = "http://runtime",
            PollIntervalSeconds = 90,
            ClientSubnetPrefix = "10.51.30."
        });

        var effective = sut.GetEffective();

        Assert.True(effective.Enabled);
        Assert.Equal("http://runtime", effective.BaseUrl);
        Assert.Equal(90, effective.PollIntervalSeconds);
        Assert.Equal("10.51.30.", effective.ClientSubnetPrefix);
    }

    [Fact]
    public void GetEffective_FallsBackToMonitorWhenNoOverride()
    {
        var sut = PiHoleRuntimeOptionsStoreTestHelper.Create(new PiHoleOptions
        {
            Enabled = true,
            BaseUrl = "http://from-env",
            BatchSize = 150
        });

        var effective = sut.GetEffective();

        Assert.Equal("http://from-env", effective.BaseUrl);
        Assert.Equal(150, effective.BatchSize);
    }

    [Fact]
    public void Apply_PersistsToDataDir_AndReloadsOnStartup()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"pihole-runtime-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDir);

        try
        {
            var sut = PiHoleRuntimeOptionsStoreTestHelper.Create(
                new PiHoleOptions { Enabled = false, BaseUrl = "http://env-default" },
                dataDir);

            sut.Apply(new PiHoleOptions
            {
                Enabled = true,
                BaseUrl = "http://saved",
                AppPassword = "secret",
                PollIntervalSeconds = 45,
                BatchSize = 250,
                LookbackSeconds = 90,
                ClientSubnetPrefix = "10.51.30."
            });

            var configPath = Path.Combine(dataDir, "pihole-runtime-config.json");
            Assert.True(File.Exists(configPath));

            var reloaded = PiHoleRuntimeOptionsStoreTestHelper.Create(
                new PiHoleOptions { Enabled = false, BaseUrl = "http://env-default" },
                dataDir,
                loadFromDisk: true);

            var effective = reloaded.GetEffective();
            Assert.True(effective.Enabled);
            Assert.Equal("http://saved", effective.BaseUrl);
            Assert.Equal("secret", effective.AppPassword);
            Assert.Equal("10.51.30.", effective.ClientSubnetPrefix);
            Assert.NotNull(reloaded.PersistedAppliedAtUtc);
        }
        finally
        {
            Directory.Delete(dataDir, recursive: true);
        }
    }

    [Fact]
    public void PersistedConfig_UsedWhenNoEnvOverrides()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"pihole-runtime-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDir);

        try
        {
            PiHoleRuntimeOptionsStoreTestHelper.Create(
                new PiHoleOptions { Enabled = false },
                dataDir)
                .Apply(new PiHoleOptions
                {
                    Enabled = true,
                    BaseUrl = "http://from-disk",
                    AppPassword = "disk-secret"
                });

            var reloaded = PiHoleRuntimeOptionsStoreTestHelper.Create(
                new PiHoleOptions { Enabled = false, BaseUrl = "http://from-monitor", AppPassword = "monitor-secret" },
                dataDir,
                loadFromDisk: true);

            var effective = reloaded.GetEffective();
            Assert.Equal("http://from-disk", effective.BaseUrl);
            Assert.Equal("disk-secret", effective.AppPassword);
            Assert.False(reloaded.HasEnvOverrides);
        }
        finally
        {
            Directory.Delete(dataDir, recursive: true);
        }
    }

    [Fact]
    public void EnvOverrides_TakePriorityOverPersistedConfig()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"pihole-runtime-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDir);

        try
        {
            PiHoleRuntimeOptionsStoreTestHelper.Create(
                new PiHoleOptions { Enabled = false },
                dataDir)
                .Apply(new PiHoleOptions
                {
                    Enabled = true,
                    BaseUrl = "http://from-disk",
                    AppPassword = "disk-secret"
                });

            var reloaded = PiHoleRuntimeOptionsStoreTestHelper.Create(
                new PiHoleOptions { Enabled = false, BaseUrl = "http://from-monitor" },
                dataDir,
                loadFromDisk: true,
                env: new Dictionary<string, string?>
                {
                    ["PIHOLE_ENABLED"] = "true",
                    ["PIHOLE_BASE_URL"] = "http://from-env"
                });

            var effective = reloaded.GetEffective();
            Assert.True(effective.Enabled);
            Assert.Equal("http://from-env", effective.BaseUrl);
            Assert.Equal("disk-secret", effective.AppPassword);
            Assert.True(reloaded.HasEnvOverrides);
        }
        finally
        {
            Directory.Delete(dataDir, recursive: true);
        }
    }
}
