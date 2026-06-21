using DataGateOpenVpnManager.Services.Proxy;
using Microsoft.Extensions.Logging.Abstractions;

namespace DataGateOpenVpnManager.Tests.Services.Proxy;

public class ProxyConnectionLifetimeServiceTests
{
    [Fact]
    public void Register_Unregister_ManagesConnections()
    {
        var service = new ProxyConnectionLifetimeService(new NoOpProxySessionAuditService(), NullLogger<ProxyConnectionLifetimeService>.Instance);
        using var cts = new CancellationTokenSource();

        service.Register("conn-1", cts);
        service.Unregister("conn-1");

        Assert.False(service.TryTerminate("conn-1", "missing"));
    }

    [Fact]
    public void TryTerminate_RecordsAuditAndCancelsToken()
    {
        var audit = new ProxySessionAuditService(
            Microsoft.Extensions.Options.Options.Create(new DataGateOpenVpnManager.Models.OpenVpnProxyOptions { SessionAudit = true }),
            NullLogger<ProxySessionAuditService>.Instance);
        var service = new ProxyConnectionLifetimeService(audit, NullLogger<ProxyConnectionLifetimeService>.Instance);
        using var cts = new CancellationTokenSource();
        service.Register("conn-1", cts);

        Assert.True(service.TryTerminate("conn-1", "zombie"));
        Assert.True(cts.IsCancellationRequested);

        var recent = audit.GetRecent(10);
        Assert.Contains(recent, e => e.Event == "proxy.terminated");
    }
}
