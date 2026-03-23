using DataGateOpenVpnManager.Services.Proxy;
using OpenVPNGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy;
using OpenVPNGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy.Enums;

namespace DataGateOpenVpnManager.Tests.Services.Proxy;

public class ActiveProxyConnectionServiceTests
{
    private static ActiveProxyConnection Conn(string id, int localPort) =>
        new()
        {
            ConnectionId = id,
            Protocol = ProxyConnectionProtocol.Tcp,
            RealClientIp = "203.0.113.5",
            RealClientPort = 40000,
            LocalProxyIp = "127.0.0.1",
            LocalProxyPort = localPort,
            TargetIp = "127.0.0.1",
            TargetPort = 1194,
            ConnectedAtUtc = DateTime.UtcNow
        };

    [Fact]
    public void TryGetByLocalProxy_ReturnsNull_WhenEmpty()
    {
        var sut = new ActiveProxyConnectionService();

        Assert.Null(sut.TryGetByLocalProxy(12345, "127.0.0.1"));
    }

    [Fact]
    public void TryGetByLocalProxy_ReturnsConnection_WhenPortAndHostMatch()
    {
        var sut = new ActiveProxyConnectionService();
        var expected = Conn("a", 45231);
        sut.Add(expected);

        var found = sut.TryGetByLocalProxy(45231, "127.0.0.1");

        Assert.Same(expected, found);
    }

    [Fact]
    public void TryGetByLocalProxy_MatchesLocalhost_AndLoopback()
    {
        var sut = new ActiveProxyConnectionService();
        var expected = Conn("a", 45231);
        sut.Add(expected);

        Assert.Same(expected, sut.TryGetByLocalProxy(45231, "localhost"));
        Assert.Same(expected, sut.TryGetByLocalProxy(45231, "::1"));
    }

    [Fact]
    public void TryGetByLocalProxy_ReturnsNull_WhenPortDoesNotMatch()
    {
        var sut = new ActiveProxyConnectionService();
        sut.Add(Conn("a", 45231));

        Assert.Null(sut.TryGetByLocalProxy(99999, "127.0.0.1"));
    }

    [Fact]
    public void TryGetByLocalProxy_ReturnsNull_WhenHostDoesNotMatch()
    {
        var sut = new ActiveProxyConnectionService();
        sut.Add(new ActiveProxyConnection
        {
            ConnectionId = "a",
            Protocol = ProxyConnectionProtocol.Tcp,
            RealClientIp = "1.1.1.1",
            RealClientPort = 1,
            LocalProxyIp = "10.0.0.5",
            LocalProxyPort = 4000,
            TargetIp = "127.0.0.1",
            TargetPort = 1194,
            ConnectedAtUtc = DateTime.UtcNow
        });

        Assert.Null(sut.TryGetByLocalProxy(4000, "127.0.0.1"));
        Assert.NotNull(sut.TryGetByLocalProxy(4000, "10.0.0.5"));
    }

    [Fact]
    public void TryGetByLocalProxy_ReturnsOne_WhenMultipleActiveWithDifferentPorts()
    {
        var sut = new ActiveProxyConnectionService();
        var first = Conn("first", 11111);
        var second = Conn("second", 22222);
        sut.Add(first);
        sut.Add(second);

        Assert.Same(first, sut.TryGetByLocalProxy(11111, "127.0.0.1"));
        Assert.Same(second, sut.TryGetByLocalProxy(22222, "127.0.0.1"));
    }

    [Fact]
    public void Remove_ThenTryGetByLocalProxy_ReturnsNull()
    {
        var sut = new ActiveProxyConnectionService();
        sut.Add(Conn("x", 50000));
        Assert.NotNull(sut.TryGetByLocalProxy(50000, "127.0.0.1"));

        sut.Remove("x");

        Assert.Null(sut.TryGetByLocalProxy(50000, "127.0.0.1"));
    }

    [Fact]
    public void NormalizeHost_MapsLocalhost_ToLoopbackCanonical()
    {
        Assert.Equal("127.0.0.1", ActiveProxyConnectionService.NormalizeHost("localhost"));
        Assert.Equal("127.0.0.1", ActiveProxyConnectionService.NormalizeHost("127.0.0.1"));
    }

    [Fact]
    public void Count_And_GetAll_ReflectConnections()
    {
        var sut = new ActiveProxyConnectionService();
        Assert.Equal(0, sut.Count);
        Assert.Empty(sut.GetAll());

        sut.Add(Conn("1", 1));
        sut.Add(Conn("2", 2));

        Assert.Equal(2, sut.Count);
        Assert.Equal(2, sut.GetAll().Count);
    }

    [Fact]
    public void TryGet_ReturnsConnection_ByConnectionId()
    {
        var sut = new ActiveProxyConnectionService();
        var c = Conn("id-1", 33333);
        sut.Add(c);

        Assert.True(sut.TryGet("id-1", out var got));
        Assert.Same(c, got);
    }
}
