using DataGateOpenVpnManager.Services.Proxy;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy.Enums;

namespace DataGateOpenVpnManager.Tests.Services.Proxy;

public class ProxyAuditDetailsTests
{
    [Fact]
    public void ForConnection_IncludesLocalAndClientEndpoints()
    {
        var details = ProxyAuditDetails.ForConnection(new ActiveProxyConnection
        {
            ConnectionId = "c1",
            Protocol = ProxyConnectionProtocol.Tcp,
            RealClientIp = "203.0.113.1",
            RealClientPort = 50000,
            LocalProxyIp = "127.0.0.1",
            LocalProxyPort = 60123,
            TargetIp = "127.0.0.1",
            TargetPort = 1194,
            ConnectedAtUtc = new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc)
        });

        Assert.Equal("Tcp", details["proto"]);
        Assert.Equal("127.0.0.1:60123", details["local"]);
        Assert.Equal("203.0.113.1:50000", details["client"]);
        Assert.Contains("2026-06-19T12:00:00", details["connectedAtUtc"]);
    }

    [Fact]
    public void ForSnapshot_ReturnsNullMarker_WhenSnapshotMissing()
    {
        var details = ProxyAuditDetails.ForSnapshot(null);
        Assert.Equal("null", details["cache"]);
    }

    [Fact]
    public void ForSnapshot_IncludesClientCountAndAge()
    {
        var fetchedAt = DateTime.UtcNow.AddSeconds(-10);
        var snapshot = new OpenVpnManagementStatusSnapshot(
            fetchedAt,
            "END",
            [new OpenVpnManagementClientEntry("cn", "127.0.0.1:1", "10.0.0.2", 1, 2, 3)],
            IsValid: true);

        var details = ProxyAuditDetails.ForSnapshot(snapshot);

        Assert.Equal("1", details["mgmtClientCount"]);
        Assert.True(double.Parse(details["cacheAgeSec"]) >= 9);
    }
}
