using DataGateOpenVpnManager.Models;
using DataGateOpenVpnManager.Services.Proxy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace DataGateOpenVpnManager.Tests.Services.Proxy;

public class ProxySessionAuditServiceTests
{
    [Fact]
    public void Record_WritesToBuffer_WhenSessionAuditEnabled()
    {
        var logger = new Mock<ILogger<ProxySessionAuditService>>();
        var service = new ProxySessionAuditService(
            Options.Create(new OpenVpnProxyOptions { SessionAudit = true, SessionAuditBufferSize = 10 }),
            logger.Object);

        service.Record(new ProxySessionAuditEntry
        {
            AtUtc = DateTime.UtcNow,
            Event = "test.event",
            ConnectionId = "conn-1",
            Decision = "ok",
            Reason = "unit-test"
        });

        var recent = service.GetRecent(5);
        Assert.Single(recent);
        Assert.Equal("test.event", recent[0].Event);
    }

    [Fact]
    public void Record_IsNoOp_WhenSessionAuditDisabled()
    {
        var service = new ProxySessionAuditService(
            Options.Create(new OpenVpnProxyOptions { SessionAudit = false, ByteDebug = false }),
            Mock.Of<ILogger<ProxySessionAuditService>>());

        service.Record(new ProxySessionAuditEntry
        {
            AtUtc = DateTime.UtcNow,
            Event = "test.event",
            Decision = "ok",
            Reason = "unit-test"
        });

        Assert.Empty(service.GetRecent(5));
    }
}
