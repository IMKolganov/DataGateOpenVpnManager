using DataGateOpenVpnManager.Services.Proxy;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy.Enums;

namespace DataGateOpenVpnManager.Tests.Services.Proxy;

public class ProxyTrafficFlowServiceTests
{
    [Theory]
    [InlineData(1_000)]
    [InlineData(10_000)]
    public void BuildBatch_WithLargeOnlinePopulation_HandlesThousandAndTenThousandUsers(int usersCount)
    {
        var service = new ProxyTrafficFlowService();
        var connectedAt = new DateTime(2026, 05, 08, 12, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < usersCount; i += 1)
        {
            service.RegisterConnection(new ActiveProxyConnection
            {
                ConnectionId = $"u-{i}",
                Protocol = ProxyConnectionProtocol.Tcp,
                RealClientIp = $"198.51.{(i / 256) % 256}.{i % 256}",
                RealClientPort = 30000 + (i % 20000),
                LocalProxyIp = "127.0.0.1",
                LocalProxyPort = 40000 + (i % 20000),
                TargetIp = "127.0.0.1",
                TargetPort = 1194,
                ConnectedAtUtc = connectedAt
            });
        }

        Parallel.For(0, usersCount, i =>
        {
            var cid = $"u-{i}";
            service.RecordTraffic(cid, ProxyTrafficFlowDirection.ClientToServer, 64, connectedAt.AddSeconds(1));
            service.RecordTraffic(cid, ProxyTrafficFlowDirection.ServerToClient, 128, connectedAt.AddSeconds(1));
        });

        var batch = service.BuildBatch(connectedAt.AddSeconds(2));
        Assert.Equal(usersCount, batch.Count);
        Assert.All(batch, item =>
        {
            Assert.True(item.IsConnected);
            Assert.Equal("connected", item.State);
            Assert.Equal(64, item.ClientToServerBytesDelta);
            Assert.Equal(128, item.ServerToClientBytesDelta);
            Assert.Equal(64, item.ClientToServerBytesTotal);
            Assert.Equal(128, item.ServerToClientBytesTotal);
        });
    }

    [Fact]
    public async Task BuildBatch_With25ConcurrentConnections_PreservesTotalsAndDeltas()
    {
        const int connectionsCount = 25;
        const int iterationsPerConnection = 400;
        const int c2sBytes = 37;
        const int s2cBytes = 53;

        var service = new ProxyTrafficFlowService();
        var connectedAt = new DateTime(2026, 05, 08, 12, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < connectionsCount; i += 1)
        {
            service.RegisterConnection(new ActiveProxyConnection
            {
                ConnectionId = $"conn-{i}",
                Protocol = ProxyConnectionProtocol.Tcp,
                RealClientIp = $"198.51.100.{i + 1}",
                RealClientPort = 50000 + i,
                LocalProxyIp = "127.0.0.1",
                LocalProxyPort = 60000 + i,
                TargetIp = "127.0.0.1",
                TargetPort = 1194,
                ConnectedAtUtc = connectedAt
            });
        }

        var tasks = Enumerable.Range(0, connectionsCount)
            .Select(i => Task.Run(() =>
            {
                var connectionId = $"conn-{i}";
                for (var n = 0; n < iterationsPerConnection; n += 1)
                {
                    service.RecordTraffic(connectionId, ProxyTrafficFlowDirection.ClientToServer, c2sBytes, connectedAt.AddSeconds(1));
                    service.RecordTraffic(connectionId, ProxyTrafficFlowDirection.ServerToClient, s2cBytes, connectedAt.AddSeconds(1));
                }
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        var batch = service.BuildBatch(connectedAt.AddSeconds(2));
        Assert.Equal(connectionsCount, batch.Count);

        var expectedC2S = (long)iterationsPerConnection * c2sBytes;
        var expectedS2C = (long)iterationsPerConnection * s2cBytes;
        foreach (var update in batch)
        {
            Assert.True(update.IsConnected);
            Assert.Equal("connected", update.State);
            Assert.Equal(expectedC2S, update.ClientToServerBytesTotal);
            Assert.Equal(expectedS2C, update.ServerToClientBytesTotal);
            Assert.Equal(expectedC2S, update.ClientToServerBytesDelta);
            Assert.Equal(expectedS2C, update.ServerToClientBytesDelta);
        }

        var secondBatch = service.BuildBatch(connectedAt.AddSeconds(3));
        Assert.Equal(connectionsCount, secondBatch.Count);
        foreach (var update in secondBatch)
        {
            Assert.Equal(0, update.ClientToServerBytesDelta);
            Assert.Equal(0, update.ServerToClientBytesDelta);
        }
    }

    [Fact]
    public void BuildBatch_ReportsIdleConnection_WhenNoTrafficForThreshold()
    {
        var service = new ProxyTrafficFlowService();
        var connectedAt = new DateTime(2026, 05, 08, 12, 0, 0, DateTimeKind.Utc);
        service.RegisterConnection(new ActiveProxyConnection
        {
            ConnectionId = "conn-1",
            Protocol = ProxyConnectionProtocol.Tcp,
            RealClientIp = "198.51.100.10",
            RealClientPort = 50000,
            LocalProxyIp = "127.0.0.1",
            LocalProxyPort = 60000,
            TargetIp = "127.0.0.1",
            TargetPort = 1194,
            ConnectedAtUtc = connectedAt
        });

        var batch = service.BuildBatch(connectedAt.AddSeconds(11));
        var update = Assert.Single(batch);
        Assert.Equal("conn-1", update.ConnectionId);
        Assert.True(update.IsConnected);
        Assert.True(update.IsIdle);
        Assert.Equal(0, update.ClientToServerBytesDelta);
        Assert.Equal(0, update.ServerToClientBytesDelta);
    }

    [Fact]
    public void BuildBatch_ResetsDeltas_AfterEmission()
    {
        var service = new ProxyTrafficFlowService();
        var connectedAt = new DateTime(2026, 05, 08, 12, 0, 0, DateTimeKind.Utc);
        service.RegisterConnection(new ActiveProxyConnection
        {
            ConnectionId = "conn-2",
            Protocol = ProxyConnectionProtocol.Udp,
            RealClientIp = "203.0.113.7",
            RealClientPort = 51000,
            LocalProxyIp = "127.0.0.1",
            LocalProxyPort = 60100,
            TargetIp = "127.0.0.1",
            TargetPort = 1194,
            ConnectedAtUtc = connectedAt
        });

        service.RecordTraffic("conn-2", ProxyTrafficFlowDirection.ClientToServer, 1200, connectedAt.AddSeconds(1));
        service.RecordTraffic("conn-2", ProxyTrafficFlowDirection.ServerToClient, 800, connectedAt.AddSeconds(2));

        var firstBatch = service.BuildBatch(connectedAt.AddSeconds(3));
        var first = Assert.Single(firstBatch);
        Assert.Equal(1200, first.ClientToServerBytesDelta);
        Assert.Equal(800, first.ServerToClientBytesDelta);
        Assert.False(first.IsIdle);

        var secondBatch = service.BuildBatch(connectedAt.AddSeconds(4));
        var second = Assert.Single(secondBatch);
        Assert.Equal(0, second.ClientToServerBytesDelta);
        Assert.Equal(0, second.ServerToClientBytesDelta);
        Assert.Equal(1200, second.ClientToServerBytesTotal);
        Assert.Equal(800, second.ServerToClientBytesTotal);
    }

    [Fact]
    public void UnregisterConnection_EmitsDisconnectedUpdate()
    {
        var service = new ProxyTrafficFlowService();
        var connectedAt = new DateTime(2026, 05, 08, 12, 0, 0, DateTimeKind.Utc);
        var disconnectedAt = connectedAt.AddSeconds(5);
        service.RegisterConnection(new ActiveProxyConnection
        {
            ConnectionId = "conn-3",
            Protocol = ProxyConnectionProtocol.Tcp,
            RealClientIp = "192.0.2.22",
            RealClientPort = 52000,
            LocalProxyIp = "127.0.0.1",
            LocalProxyPort = 60200,
            TargetIp = "127.0.0.1",
            TargetPort = 1194,
            ConnectedAtUtc = connectedAt
        });

        service.RecordTraffic("conn-3", ProxyTrafficFlowDirection.ClientToServer, 512, connectedAt.AddSeconds(2));
        service.UnregisterConnection("conn-3", disconnectedAt);

        var batch = service.BuildBatch(disconnectedAt);
        var update = Assert.Single(batch);
        Assert.Equal("disconnected", update.State);
        Assert.False(update.IsConnected);
        Assert.Equal(512, update.ClientToServerBytesTotal);
    }

    [Fact]
    public void BuildBatch_IncludesIdentityMetadata_WhenProvided()
    {
        var service = new ProxyTrafficFlowService();
        var connectedAt = new DateTime(2026, 05, 08, 12, 0, 0, DateTimeKind.Utc);
        service.RegisterConnection(new ActiveProxyConnection
        {
            ConnectionId = "conn-4",
            Protocol = ProxyConnectionProtocol.Tcp,
            RealClientIp = "198.18.0.9",
            RealClientPort = 53000,
            LocalProxyIp = "127.0.0.1",
            LocalProxyPort = 60300,
            TargetIp = "127.0.0.1",
            TargetPort = 1194,
            ConnectedAtUtc = connectedAt
        }, new ProxyConnectionIdentity
        {
            ClientRef = "client-app-42",
            UserId = "1001",
            Username = "alice",
            Email = "alice@example.com"
        });

        var batch = service.BuildBatch(connectedAt.AddSeconds(1));
        var update = Assert.Single(batch);
        Assert.Equal("client-app-42", update.ClientRef);
        Assert.Equal("1001", update.UserId);
        Assert.Equal("alice", update.Username);
        Assert.Equal("alice@example.com", update.Email);
    }
}
