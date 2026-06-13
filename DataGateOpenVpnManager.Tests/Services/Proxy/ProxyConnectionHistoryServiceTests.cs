using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy.Enums;
using DataGateOpenVpnManager.Services.Proxy;

namespace DataGateOpenVpnManager.Tests.Services.Proxy;

public class ProxyConnectionHistoryServiceTests
{
    private static ProxyConnectionHistoryItem Item(string connectionId) =>
        new()
        {
            ConnectionId = connectionId,
            Protocol = ProxyConnectionProtocol.Tcp,
            RealClientIp = "198.51.100.2",
            RealClientPort = 0,
            LocalProxyIp = "127.0.0.1",
            LocalProxyPort = 1234,
            TargetIp = "127.0.0.1",
            TargetPort = 1194,
            EventType = ProxyConnectionEventType.Connected,
            CreatedAtUtc = DateTime.UtcNow
        };

    [Fact]
    public void Add_GetAll_PreservesOrder_And_Count()
    {
        var sut = new ProxyConnectionHistoryService();
        sut.Add(Item("a"));
        sut.Add(Item("b"));

        Assert.Equal(2, sut.Count);
        var all = sut.GetAll().ToList();
        Assert.Equal(2, all.Count);
        Assert.Equal("a", all[0].ConnectionId);
        Assert.Equal("b", all[1].ConnectionId);
    }

    [Fact]
    public void GetAll_ReturnsSnapshot_Copy()
    {
        var sut = new ProxyConnectionHistoryService();
        sut.Add(Item("one"));

        var first = sut.GetAll();
        sut.Add(Item("two"));

        Assert.Single(first);
        Assert.Equal(2, sut.GetAll().Count);
    }
}
