using DataGateOpenVpnManager.Services.OpenVpnTelnet;
using DataGateOpenVpnManager.Services.Proxy;
using DataGateOpenVpnManager.Tests.Services.OpenVpnTelnet.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace DataGateOpenVpnManager.Tests.Services.Proxy;

public class OpenVpnManagementStatusCacheTests
{
    [Theory]
    [InlineData("127.0.0.1:53188")]
    [InlineData("tcp4-server:127.0.0.1:53188")]
    public async Task RefreshAsync_ParsesLegacyAndOpenVpn27ClientList(string realAddress)
    {
        var telnet = new FakeTelnetClient();
        var management = new OpenVpnManagementSignalService(telnet, NullLogger<CommandQueue>.Instance);
        var cache = new OpenVpnManagementStatusCache(
            management,
            new NoOpProxySessionAuditService(),
            NullLogger<OpenVpnManagementStatusCache>.Instance);

        var refreshTask = cache.RefreshAsync(CancellationToken.None);
        telnet.SimulateIncomingData(
            $"CLIENT_LIST\tadg-75-test\t{realAddress}\t10.51.15.8\t\t0\t0\t1782816170\tUNDEF\nEND");
        await refreshTask;

        var snapshot = cache.GetSnapshot();
        Assert.NotNull(snapshot);
        Assert.True(snapshot!.IsValid);
        var client = Assert.Single(snapshot.Clients);
        Assert.Equal("adg-75-test", client.CommonName);
        Assert.Equal(realAddress, client.RealAddress);
        Assert.Equal("10.51.15.8", client.VirtualAddress);
    }
}
