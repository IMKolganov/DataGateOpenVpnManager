using DataGateOpenVpnManager.Services.Proxy;

namespace DataGateOpenVpnManager.Tests.Services.Proxy;

public class OpenVpnManagementStatusParserVirtualAddressTests
{
    [Fact]
    public void FindByVirtualAddress_MatchesCaseInsensitive()
    {
        var clients = new[]
        {
            new OpenVpnManagementClientEntry("alice", "1.1.1.1:443", "10.51.30.10", 0, 0, 0)
        };

        var match = OpenVpnManagementStatusParser.FindByVirtualAddress(clients, "10.51.30.10");

        Assert.NotNull(match);
        Assert.Equal("alice", match!.CommonName);
    }
}
