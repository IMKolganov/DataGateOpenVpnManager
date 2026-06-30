using DataGateOpenVpnManager.Services.OpenVpnTls;

namespace DataGateOpenVpnManager.Tests.Services.OpenVpnTls;

public class OpenVpnTlsPeerParserTests
{
    [Theory]
    [InlineData(
        "TLS Error: tls-crypt unwrapping failed from [AF_INET]185.200.116.35:35784",
        "185.200.116.35:35784",
        "185.200.116.35",
        35784,
        false)]
    [InlineData(
        "TLS Error: tls-crypt unwrapping failed from [AF_INET]127.0.0.1:41810",
        "127.0.0.1:41810",
        "127.0.0.1",
        41810,
        true)]
    [InlineData(
        "packet authentication failed from [AF_INET6]::1:51234",
        "::1:51234",
        "::1",
        51234,
        true)]
    public void TryExtractPeer_ParsesOpenVpnLines(string line, string peer, string host, int port, bool loopback)
    {
        var ok = OpenVpnTlsPeerParser.TryExtractPeer(line, out var parsedPeer, out var parsedHost, out var parsedPort);

        Assert.True(ok);
        Assert.Equal(peer, parsedPeer);
        Assert.Equal(host, parsedHost);
        Assert.Equal(port, parsedPort);
        Assert.Equal(loopback, OpenVpnTlsPeerParser.IsLoopbackHost(parsedHost));
    }
}
