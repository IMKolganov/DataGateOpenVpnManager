using DataGateOpenVpnManager.Controllers;
using DataGateOpenVpnManager.Models;
using DataGateOpenVpnManager.Services.PiHole;
using DataGateMonitor.SharedModels.Responses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataGateOpenVpnManager.Tests.Controllers;

public class PiHoleControllerTests
{
    [Fact]
    public void PutConfig_MergesPasswordAndAppliesRuntimeStore()
    {
        var monitor = new TestOptionsMonitor(new PiHoleOptions
        {
            Enabled = false,
            BaseUrl = "http://old",
            AppPassword = "stored-secret",
            PollIntervalSeconds = 60,
            BatchSize = 100,
            LookbackSeconds = 120
        });
        var store = new PiHoleRuntimeOptionsStore(monitor);
        var api = new Mock<IPiHoleApiClient>();
        var status = new PiHoleCollectorStatusStore();
        var cursor = new Mock<IPiHoleQueryCursorStore>();
        var controller = new PiHoleController(
            store,
            api.Object,
            status,
            cursor.Object,
            NullLogger<PiHoleController>.Instance);

        var result = controller.PutConfig(new PiHoleOptionsDto
        {
            Enabled = true,
            BaseUrl = "http://pi-hole:8080",
            AppPassword = "********",
            PollIntervalSeconds = 45,
            BatchSize = 250,
            LookbackSeconds = 90,
            ClientSubnetPrefix = "10.51.30."
        });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<PiHoleOptionsDto>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.True(envelope.Data!.Enabled);
        Assert.Equal("http://pi-hole:8080", envelope.Data.BaseUrl);
        Assert.True(envelope.Data.HasAppPassword);

        var effective = store.GetEffective();
        Assert.Equal("stored-secret", effective.AppPassword);
        Assert.Equal("10.51.30.", effective.ClientSubnetPrefix);
    }

    private sealed class TestOptionsMonitor(PiHoleOptions current) : IOptionsMonitor<PiHoleOptions>
    {
        public PiHoleOptions CurrentValue { get; } = current;
        public PiHoleOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<PiHoleOptions, string?> listener) => null;
    }
}
