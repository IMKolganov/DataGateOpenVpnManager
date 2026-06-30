using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy.Enums;
using DataGateOpenVpnManager.Services.OpenVpnTls;
using DataGateOpenVpnManager.Services.Proxy;

namespace DataGateOpenVpnManager.Tests.Services.OpenVpnTls;

public class OpenVpnTlsErrorClassifierTests
{
    [Fact]
    public void Classify_MarksPublicPeer_AsExternalProbe()
    {
        var classifier = CreateClassifier();
        var line = "TLS Error: tls-crypt unwrapping failed from [AF_INET]185.200.116.35:35784";

        var ctx = classifier.Classify(line);

        Assert.Equal(OpenVpnTlsErrorOrigin.ExternalProbe, ctx.Origin);
        Assert.Equal("185.200.116.35:35784", ctx.Peer);
    }

    [Fact]
    public void Classify_MarksLoopbackWithProxy_AsAppViaProxy_WithIdentity()
    {
        var active = new ActiveProxyConnectionService();
        var traffic = new ProxyTrafficFlowService();
        var classifier = new OpenVpnTlsErrorClassifier(active, traffic);

        var connection = new ActiveProxyConnection
        {
            ConnectionId = "c1",
            Protocol = ProxyConnectionProtocol.Udp,
            RealClientIp = "203.0.113.9",
            RealClientPort = 44000,
            LocalProxyIp = "127.0.0.1",
            LocalProxyPort = 41810,
            TargetIp = "127.0.0.1",
            TargetPort = 1194,
            ConnectedAtUtc = DateTime.UtcNow
        };
        active.Add(connection);
        traffic.RegisterConnection(connection, new ProxyConnectionIdentity
        {
            ClientRef = "ext-42",
            UserId = "1001",
            Username = "alice",
            UserAgent = "DataGate/1.2 Android"
        });

        var line = "TLS Error: tls-crypt unwrapping failed from [AF_INET]127.0.0.1:41810";
        var ctx = classifier.Classify(line);

        Assert.Equal(OpenVpnTlsErrorOrigin.AppViaProxy, ctx.Origin);
        Assert.Equal("ext-42", ctx.ClientRef);
        Assert.Equal("1001", ctx.UserId);
        Assert.Equal("alice", ctx.Username);
        Assert.Equal("DataGate/1.2 Android", ctx.UserAgent);
        Assert.Equal("203.0.113.9", ctx.RealClientIp);
    }

    [Fact]
    public void Classify_MarksLoopbackWithoutProxy_AsLocalUnknown()
    {
        var classifier = CreateClassifier();
        var line = "TLS Error: tls-crypt unwrapping failed from [AF_INET]127.0.0.1:59999";

        var ctx = classifier.Classify(line);

        Assert.Equal(OpenVpnTlsErrorOrigin.LocalUnknown, ctx.Origin);
    }

    private static OpenVpnTlsErrorClassifier CreateClassifier() =>
        new(new ActiveProxyConnectionService(), new ProxyTrafficFlowService());
}
