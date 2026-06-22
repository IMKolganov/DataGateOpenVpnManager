using DataGateOpenVpnManager.Hubs;
using DataGateOpenVpnManager.Models;
using DataGateOpenVpnManager.Services.PiHole;
using DataGateOpenVpnManager.Services.Proxy;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace DataGateOpenVpnManager.Tests.Services.PiHole;

public class PiHoleQueryCollectorHostedServiceTests
{
    [Fact]
    public async Task CollectOnceAsync_BroadcastsEnrichedBatch()
    {
        var api = new Mock<IPiHoleApiClient>();
        api.Setup(x => x.GetQueriesSinceAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PiHoleQueryFetchResult
            {
                TotalFromApi = 1,
                Records = new[]
                {
                    new PiHoleQueryRecord(10, "10.51.30.3", "openai.com", "A", "FORWARDED", DateTimeOffset.UtcNow)
                }
            });

        var cursor = new Mock<IPiHoleQueryCursorStore>();
        cursor.Setup(x => x.GetLastUntilUtc()).Returns((DateTimeOffset?)null);

        var cache = new Mock<IOpenVpnManagementStatusCache>();
        cache.Setup(x => x.GetSnapshot()).Returns(new OpenVpnManagementStatusSnapshot(
            DateTime.UtcNow,
            "raw",
            new[]
            {
                new OpenVpnManagementClientEntry("cn-1", "1.2.3.4:1234", "10.51.30.3", 0, 0, 0)
            },
            true));

        var clientProxy = new Mock<IClientProxy>();
        clientProxy.Setup(c => c.SendCoreAsync(
                "DnsQueriesReceived",
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.All).Returns(clientProxy.Object);
        var hub = new Mock<IHubContext<OpenVpnEventHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);

        var store = new PiHoleRuntimeOptionsStore(new TestOptionsMonitor(new PiHoleOptions()));
        store.Apply(new PiHoleOptions { BatchSize = 50, LookbackSeconds = 60, Enabled = true });

        var sut = new PiHoleQueryCollectorHostedService(
            store,
            api.Object,
            new PiHoleClientIdentityResolver(),
            cursor.Object,
            new PiHoleCollectorStatusStore(),
            cache.Object,
            hub.Object,
            NullLogger<PiHoleQueryCollectorHostedService>.Instance);

        var count = await sut.CollectOnceAsync(
            new PiHoleOptions { BatchSize = 50, LookbackSeconds = 60 },
            CancellationToken.None);

        Assert.Equal(1, count);
        cursor.Verify(x => x.SaveLastUntilUtc(It.IsAny<DateTimeOffset>()), Times.Once);
        clientProxy.Verify(
            c => c.SendCoreAsync("DnsQueriesReceived", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CollectOnceAsync_AdvancesCursor_WhenNoRecordsAfterFilter()
    {
        var api = new Mock<IPiHoleApiClient>();
        api.Setup(x => x.GetQueriesSinceAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PiHoleQueryFetchResult { TotalFromApi = 3, Records = [] });

        var cursor = new Mock<IPiHoleQueryCursorStore>();
        cursor.Setup(x => x.GetLastUntilUtc()).Returns((DateTimeOffset?)null);

        var status = new PiHoleCollectorStatusStore();
        var sut = CreateSut(api.Object, cursor.Object, status, hubClients: null);

        var count = await sut.CollectOnceAsync(
            new PiHoleOptions { BatchSize = 50, LookbackSeconds = 60 },
            CancellationToken.None);

        Assert.Equal(0, count);
        cursor.Verify(x => x.SaveLastUntilUtc(It.IsAny<DateTimeOffset>()), Times.Once);
        var snapshot = status.GetSnapshot();
        Assert.Equal(3, snapshot.LastPollQueriesFetched);
        Assert.Equal(0, snapshot.LastPollQueriesForwarded);
        Assert.Null(snapshot.LastPollError);
    }

    [Fact]
    public async Task CollectOnceAsync_DoesNotBroadcast_WhenNoClientMapping()
    {
        var api = new Mock<IPiHoleApiClient>();
        api.Setup(x => x.GetQueriesSinceAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PiHoleQueryFetchResult
            {
                TotalFromApi = 2,
                Records =
                [
                    new PiHoleQueryRecord(11, "10.51.30.99", "unknown.test", "A", "FORWARDED", DateTimeOffset.UtcNow)
                ]
            });

        var cursor = new Mock<IPiHoleQueryCursorStore>();
        cursor.Setup(x => x.GetLastUntilUtc()).Returns((DateTimeOffset?)null);

        var cache = new Mock<IOpenVpnManagementStatusCache>();
        cache.Setup(x => x.GetSnapshot()).Returns(new OpenVpnManagementStatusSnapshot(
            DateTime.UtcNow,
            "raw",
            [],
            true));

        var clientProxy = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.All).Returns(clientProxy.Object);
        var hub = new Mock<IHubContext<OpenVpnEventHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);

        var status = new PiHoleCollectorStatusStore();
        var sut = CreateSut(api.Object, cursor.Object, status, cache.Object, hub);

        var count = await sut.CollectOnceAsync(
            new PiHoleOptions { BatchSize = 50, LookbackSeconds = 60 },
            CancellationToken.None);

        Assert.Equal(0, count);
        clientProxy.Verify(
            c => c.SendCoreAsync("DnsQueriesReceived", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
        var snapshot = status.GetSnapshot();
        Assert.Equal(1, snapshot.LastPollQueriesAfterFilter);
        Assert.Equal(0, snapshot.LastPollQueriesEnriched);
    }

    private static PiHoleQueryCollectorHostedService CreateSut(
        IPiHoleApiClient api,
        IPiHoleQueryCursorStore cursor,
        IPiHoleCollectorStatusStore status,
        IOpenVpnManagementStatusCache? cache = null,
        Mock<IHubContext<OpenVpnEventHub>>? hubClients = null)
    {
        var managementCache = cache ?? CreateDefaultCache();
        var hub = hubClients ?? CreateDefaultHub();

        return new PiHoleQueryCollectorHostedService(
            new PiHoleRuntimeOptionsStore(new TestOptionsMonitor(new PiHoleOptions { Enabled = true })),
            api,
            new PiHoleClientIdentityResolver(),
            cursor,
            status,
            managementCache,
            hub.Object,
            NullLogger<PiHoleQueryCollectorHostedService>.Instance);
    }

    private static IOpenVpnManagementStatusCache CreateDefaultCache()
    {
        var cache = new Mock<IOpenVpnManagementStatusCache>();
        cache.Setup(x => x.GetSnapshot()).Returns(new OpenVpnManagementStatusSnapshot(
            DateTime.UtcNow,
            "raw",
            [new OpenVpnManagementClientEntry("cn-1", "1.2.3.4:1234", "10.51.30.3", 0, 0, 0)],
            true));
        return cache.Object;
    }

    private static Mock<IHubContext<OpenVpnEventHub>> CreateDefaultHub()
    {
        var clientProxy = new Mock<IClientProxy>();
        clientProxy.Setup(c => c.SendCoreAsync(
                "DnsQueriesReceived",
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.All).Returns(clientProxy.Object);
        var hub = new Mock<IHubContext<OpenVpnEventHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);
        return hub;
    }

    private sealed class TestOptionsMonitor(PiHoleOptions current) : IOptionsMonitor<PiHoleOptions>
    {
        public PiHoleOptions CurrentValue { get; } = current;
        public PiHoleOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<PiHoleOptions, string?> listener) => null;
    }
}
