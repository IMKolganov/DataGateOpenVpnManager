using DataGateOpenVpnManager.Models;
using DataGateOpenVpnManager.Services.PiHole;
using Microsoft.Extensions.Options;

namespace DataGateOpenVpnManager.Tests.Services.PiHole;

public class PiHoleRuntimeOptionsStoreTests
{
    [Fact]
    public void GetEffective_ReturnsOverrideAfterApply()
    {
        var monitor = new TestOptionsMonitor(new PiHoleOptions
        {
            Enabled = false,
            BaseUrl = "http://env-default",
            PollIntervalSeconds = 30
        });
        var sut = new PiHoleRuntimeOptionsStore(monitor);

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
        var monitor = new TestOptionsMonitor(new PiHoleOptions
        {
            Enabled = true,
            BaseUrl = "http://from-env",
            BatchSize = 150
        });
        var sut = new PiHoleRuntimeOptionsStore(monitor);

        var effective = sut.GetEffective();

        Assert.Equal("http://from-env", effective.BaseUrl);
        Assert.Equal(150, effective.BatchSize);
    }

    private sealed class TestOptionsMonitor(PiHoleOptions current) : IOptionsMonitor<PiHoleOptions>
    {
        public PiHoleOptions CurrentValue { get; } = current;
        public PiHoleOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<PiHoleOptions, string?> listener) => null;
    }
}
