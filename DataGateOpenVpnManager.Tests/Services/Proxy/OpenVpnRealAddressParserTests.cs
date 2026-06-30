using DataGateOpenVpnManager.Services.Proxy;

namespace DataGateOpenVpnManager.Tests.Services.Proxy;

public class OpenVpnRealAddressParserTests
{
    [Theory]
    [InlineData("127.0.0.1:53188", "127.0.0.1", 53188)]
    [InlineData("tcp4-server:127.0.0.1:53188", "127.0.0.1", 53188)]
    [InlineData("udp4-server:127.0.0.1:51932", "127.0.0.1", 51932)]
    [InlineData("tcp4-server:203.0.113.1:443", "203.0.113.1", 443)]
    [InlineData("[::1]:50000", "::1", 50000)]
    [InlineData("tcp6-server:[::1]:50000", "::1", 50000)]
    [InlineData("localhost:54321", "localhost", 54321)]
    [InlineData(" 127.0.0.1:41810 ", "127.0.0.1", 41810)]
    public void TryParseHostPort_ParsesLegacyAndOpenVpn27Formats(string input, string expectedHost, int expectedPort)
    {
        var ok = OpenVpnRealAddressParser.TryParseHostPort(input, out var host, out var port);

        Assert.True(ok);
        Assert.Equal(expectedHost, host);
        Assert.Equal(expectedPort, port);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-endpoint")]
    [InlineData("tcp4-server:127.0.0.1")]
    [InlineData("127.0.0.1:0")]
    [InlineData("127.0.0.1:70000")]
    public void TryParseHostPort_RejectsInvalidInput(string? input)
    {
        Assert.False(OpenVpnRealAddressParser.TryParseHostPort(input, out _, out _));
    }

    [Theory]
    [InlineData("tcp4-server:127.0.0.1:53188", "127.0.0.1:53188")]
    [InlineData("127.0.0.1:53188", "127.0.0.1:53188")]
    [InlineData("localhost:54321", "127.0.0.1:54321")]
    [InlineData("[::1]:50000", "127.0.0.1:50000")]
    [InlineData("tcp4-server:203.0.113.1:443", null)]
    [InlineData("203.0.113.1:443", null)]
    public void CanonicalizeLoopback_ReturnsStableLoopbackFormat(string input, string? expected)
    {
        Assert.Equal(expected, OpenVpnRealAddressParser.CanonicalizeLoopback(input));
    }

    [Fact]
    public void FindByLocalProxyPort_MatchesOpenVpn27Tcp4ServerFormat()
    {
        var clients = new[]
        {
            new OpenVpnManagementClientEntry(
                "adg-75-test",
                "tcp4-server:127.0.0.1:53188",
                "10.51.15.8",
                0,
                0,
                0)
        };

        var match = OpenVpnManagementStatusParser.FindByLocalProxyPort(clients, "127.0.0.1", 53188);

        Assert.NotNull(match);
        Assert.Equal("adg-75-test", match!.CommonName);
    }

    [Fact]
    public void FindByLocalProxyPort_DoesNotMatchDifferentPort()
    {
        var clients = new[]
        {
            new OpenVpnManagementClientEntry("cn", "tcp4-server:127.0.0.1:53188", "10.51.15.8", 0, 0, 0)
        };

        Assert.Null(OpenVpnManagementStatusParser.FindByLocalProxyPort(clients, "127.0.0.1", 53189));
    }

    [Fact]
    public void FindByLocalProxyPort_DoesNotMatchExternalRealAddress()
    {
        var clients = new[]
        {
            new OpenVpnManagementClientEntry("cn", "tcp4-server:203.0.113.1:443", "10.51.15.8", 0, 0, 0)
        };

        Assert.Null(OpenVpnManagementStatusParser.FindByLocalProxyPort(clients, "127.0.0.1", 443));
    }

    [Theory]
    [InlineData("tcp4-server:127.0.0.1:53188", "127.0.0.1:53188")]
    [InlineData("tcp4-server:203.0.113.1:443", "203.0.113.1:443")]
    public void NormalizeEndpoint_StripsOpenVpn27SocketPrefix(string input, string expected)
    {
        Assert.Equal(expected, OpenVpnRealAddressParser.NormalizeEndpoint(input));
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("203.0.113.1", false)]
    public void IsLoopbackHost_DetectsLoopbackEndpoints(string host, bool expected)
    {
        Assert.Equal(expected, OpenVpnRealAddressParser.IsLoopbackHost(host));
    }
}
