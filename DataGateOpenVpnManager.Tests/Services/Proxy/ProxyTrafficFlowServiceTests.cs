using DataGateOpenVpnManager.Services.Proxy;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy.Enums;

namespace DataGateOpenVpnManager.Tests.Services.Proxy;

public class ProxyTrafficFlowServiceTests
{
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
