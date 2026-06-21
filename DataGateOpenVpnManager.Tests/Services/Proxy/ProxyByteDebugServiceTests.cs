using DataGateOpenVpnManager.Models;
using DataGateOpenVpnManager.Services.Proxy;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace DataGateOpenVpnManager.Tests.Services.Proxy;

public class ProxyByteDebugServiceTests
{
    [Fact]
    public async Task CompareAndLogAsync_LogsComparison_WhenManagementClientFound()
    {
        var telnet = new DataGateOpenVpnManager.Tests.Services.OpenVpnTelnet.Fakes.FakeTelnetClient();
        var management = new DataGateOpenVpnManager.Services.OpenVpnTelnet.OpenVpnManagementSignalService(
            telnet,
            Mock.Of<ILogger<DataGateOpenVpnManager.Services.OpenVpnTelnet.CommandQueue>>());
        var realCache = new OpenVpnManagementStatusCache(management, new NoOpProxySessionAuditService(), Mock.Of<ILogger<OpenVpnManagementStatusCache>>());
        var refreshTask = realCache.RefreshAsync(CancellationToken.None);
        await Task.Delay(50);
        telnet.SimulateIncomingData(
            "CLIENT_LIST\tadg-77\t127.0.0.1:60123\t10.51.16.3\t\t1000\t2000\t1748337500\tUNDEF\nEND\n");
        await refreshTask;

        var logger = new Mock<ILogger<ProxyByteDebugService>>();
        var service = new ProxyByteDebugService(
            Options.Create(new OpenVpnProxyOptions { ByteDebug = true, ByteDebugWarnDeltaBytes = 4096 }),
            realCache,
            new NoOpProxySessionAuditService(),
            logger.Object);

        await service.CompareAndLogAsync(new ProxyTrafficFlowUpdate
        {
            ConnectionId = "conn-1",
            Protocol = ProxyConnectionProtocol.Udp,
            State = "disconnected",
            IsConnected = false,
            IsIdle = true,
            LocalProxyIp = "127.0.0.1",
            LocalProxyPort = 60123,
            ClientToServerBytesTotal = 1000,
            ServerToClientBytesTotal = 2000,
            ConnectedAtUtc = DateTime.UtcNow,
            LastActivityAtUtc = DateTime.UtcNow,
            EmittedAtUtc = DateTime.UtcNow
        }, "periodic", CancellationToken.None);

        logger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("[ProxyByteDebug]")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void ReportDisconnect_NoOp_WhenByteDebugDisabled()
    {
        var cache = new OpenVpnManagementStatusCache(null!, new NoOpProxySessionAuditService(), Mock.Of<ILogger<OpenVpnManagementStatusCache>>());
        var service = new ProxyByteDebugService(
            Options.Create(new OpenVpnProxyOptions { ByteDebug = false }),
            cache,
            new NoOpProxySessionAuditService(),
            Mock.Of<ILogger<ProxyByteDebugService>>());

        service.ReportDisconnect(new ProxyTrafficFlowUpdate
        {
            ConnectionId = "conn-1",
            Protocol = ProxyConnectionProtocol.Tcp,
            State = "disconnected",
            IsConnected = false,
            IsIdle = true,
            LocalProxyIp = "127.0.0.1",
            LocalProxyPort = 60123,
            ConnectedAtUtc = DateTime.UtcNow,
            LastActivityAtUtc = DateTime.UtcNow,
            EmittedAtUtc = DateTime.UtcNow
        });
    }
}
