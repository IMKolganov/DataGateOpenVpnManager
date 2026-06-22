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

        var statusSnapshot = status.GetSnapshot();
        Assert.NotNull(statusSnapshot.RuntimeConfigAppliedAtUtc);
    }

    [Fact]
    public void GetConfig_MasksPasswordInDto()
    {
        var monitor = new TestOptionsMonitor(new PiHoleOptions
        {
            Enabled = true,
            BaseUrl = "http://pi-hole:8080",
            AppPassword = "stored-secret"
        });
        var store = new PiHoleRuntimeOptionsStore(monitor);
        var controller = CreateController(store);

        var result = controller.GetConfig();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<PiHoleOptionsDto>>(ok.Value);
        Assert.Equal("********", envelope.Data!.AppPassword);
        Assert.True(envelope.Data.HasAppPassword);
    }

    [Fact]
    public async Task GetDiagnostics_ReturnsFactoryPayload()
    {
        var monitor = new TestOptionsMonitor(new PiHoleOptions
        {
            Enabled = true,
            BaseUrl = "http://pi-hole:8080",
            AppPassword = "secret",
            PollIntervalSeconds = 60
        });
        var store = new PiHoleRuntimeOptionsStore(monitor);
        var status = new PiHoleCollectorStatusStore();
        status.SetCollectorRunning(true);
        status.RecordPollSuccess(new PiHolePollSuccessResult
        {
            AtUtc = DateTimeOffset.UtcNow,
            QueriesFetched = 5,
            QueriesAfterFilter = 2,
            QueriesEnriched = 1,
            QueriesForwarded = 1,
            CursorUntilUtc = DateTimeOffset.UtcNow
        });

        var api = new Mock<IPiHoleApiClient>();
        api.Setup(x => x.ProbeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, 2, (string?)null));

        var cursor = new Mock<IPiHoleQueryCursorStore>();
        cursor.Setup(x => x.GetLastUntilUtc()).Returns(DateTimeOffset.UtcNow.AddMinutes(-1));

        var controller = new PiHoleController(store, api.Object, status, cursor.Object, NullLogger<PiHoleController>.Instance);
        var result = await controller.GetDiagnostics(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<DataGateMonitor.SharedModels.DataGateOpenVpnManager.Diagnostics.Responses.PiHoleDiagnosticsResponse>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.True(envelope.Data!.Authenticated);
        Assert.Equal(1, envelope.Data.LastPollQueriesForwarded);
        Assert.Equal(2, envelope.Data.SampleQueryCount);
    }

    private static PiHoleController CreateController(IPiHoleRuntimeOptionsStore store)
    {
        return new PiHoleController(
            store,
            new Mock<IPiHoleApiClient>().Object,
            new PiHoleCollectorStatusStore(),
            new Mock<IPiHoleQueryCursorStore>().Object,
            NullLogger<PiHoleController>.Instance);
    }

    private sealed class TestOptionsMonitor(PiHoleOptions current) : IOptionsMonitor<PiHoleOptions>
    {
        public PiHoleOptions CurrentValue { get; } = current;
        public PiHoleOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<PiHoleOptions, string?> listener) => null;
    }
}
