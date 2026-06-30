using DataGateOpenVpnManager.Services.Proxy;

namespace DataGateOpenVpnManager.Tests.Services.Proxy;

public class OpenVpnManagementStatusParserTests
{
    private const string SampleStatus =
        """
        TITLE	OpenVPN 2.6.12
        TIME	Thu Jun 19 12:00:00 2026	1748337600
        CLIENT_LIST	adg-77	127.0.0.1:54321	10.51.16.3		1234567	9876543	1748337500	UNDEF
        CLIENT_LIST	other	198.51.100.20:1194	10.51.16.4		100	200	1748337500	UNDEF
        ROUTING_TABLE
        GLOBAL_STATS
        END
        """;

    [Fact]
    public void ParseClientList_ParsesStatus3Rows()
    {
        var clients = OpenVpnManagementStatusParser.ParseClientList(SampleStatus);
        Assert.Equal(2, clients.Count);

        var first = clients[0];
        Assert.Equal("adg-77", first.CommonName);
        Assert.Equal("127.0.0.1:54321", first.RealAddress);
        Assert.Equal("10.51.16.3", first.VirtualAddress);
        Assert.Equal(1_234_567, first.BytesReceived);
        Assert.Equal(9_876_543, first.BytesSent);
    }

    [Fact]
    public void FindByLocalProxyPort_MatchesLoopbackClient()
    {
        var clients = OpenVpnManagementStatusParser.ParseClientList(SampleStatus);
        var match = OpenVpnManagementStatusParser.FindByLocalProxyPort(clients, "127.0.0.1", 54321);

        Assert.NotNull(match);
        Assert.Equal("adg-77", match!.CommonName);
    }

    [Theory]
    [InlineData("localhost", "127.0.0.1", 54321)]
    [InlineData("127.0.0.1", "localhost", 54321)]
    public void FindByLocalProxyPort_TreatsLoopbackHostsAsEqual(string localIp, string realAddressIp, int port)
    {
        var status =
            $"CLIENT_LIST	test	{realAddressIp}:{port}	10.51.16.3		10	20	1748337500	UNDEF\n";
        var clients = OpenVpnManagementStatusParser.ParseClientList(status);
        var match = OpenVpnManagementStatusParser.FindByLocalProxyPort(clients, localIp, port);
        Assert.NotNull(match);
    }

    [Fact]
    public void ParseClientList_ParsesOpenVpn27RealAddressFormat()
    {
        const string status =
            """
            CLIENT_LIST	adg-75-test	tcp4-server:127.0.0.1:53188	10.51.15.8		172526	374581	1782816170	UNDEF
            END
            """;

        var clients = OpenVpnManagementStatusParser.ParseClientList(status);

        Assert.Single(clients);
        Assert.Equal("adg-75-test", clients[0].CommonName);
        Assert.Equal("tcp4-server:127.0.0.1:53188", clients[0].RealAddress);
        Assert.Equal("10.51.15.8", clients[0].VirtualAddress);
    }

    [Fact]
    public void FindByLocalProxyPort_MatchesOpenVpn27Status3Row()
    {
        const string status =
            """
            CLIENT_LIST	adg-75-test	tcp4-server:127.0.0.1:53188	10.51.15.8		0	0	1782816170	UNDEF
            END
            """;

        var clients = OpenVpnManagementStatusParser.ParseClientList(status);
        var match = OpenVpnManagementStatusParser.FindByLocalProxyPort(clients, "127.0.0.1", 53188);

        Assert.NotNull(match);
        Assert.Equal("adg-75-test", match!.CommonName);
    }

    [Fact]
    public void ProxyByteComparison_ComputesDeltas()
    {
        var comparison = ProxyByteComparison.Create(1_000, 2_000, 950, 2_100);
        Assert.Equal(50, comparison.DeltaClientToServer);
        Assert.Equal(-100, comparison.DeltaServerToClient);
        Assert.True(comparison.HasMaterialDelta(50));
        Assert.False(comparison.HasMaterialDelta(101));
    }
}
