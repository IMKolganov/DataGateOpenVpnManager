using DataGateOpenVpnManager.Models;
using DataGateOpenVpnManager.Services.OpenVpnTelnet;
using DataGateOpenVpnManager.Services.Proxy;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DataGateOpenVpnManager.Tests.Services.Proxy;

public class ProxyByteDebugMonitorServiceTests
{
    [Fact]
    public async Task ExecuteAsync_WhenDisabled_IdlesWithoutThrowing()
    {
        var service = CreateService(new OpenVpnProxyOptions());
        await service.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEnabled_ComparesActiveConnections()
    {
        var active = new ActiveProxyConnectionService();
        var connection = new ActiveProxyConnection
        {
            ConnectionId = "c1",
            Protocol = ProxyConnectionProtocol.Udp,
            RealClientIp = "203.0.113.1",
            RealClientPort = 50000,
            LocalProxyIp = "127.0.0.1",
            LocalProxyPort = 60123,
            TargetIp = "127.0.0.1",
            TargetPort = 1194,
            ConnectedAtUtc = DateTime.UtcNow
        };
        active.Add(connection);

        var flow = new ProxyTrafficFlowService();
        flow.RegisterConnection(connection);
        flow.RecordTraffic("c1", ProxyTrafficFlowDirection.ClientToServer, 128);

        var byteDebug = new ProxyByteDebugService(
            Options.Create(new OpenVpnProxyOptions { ByteDebug = true }),
            new OpenVpnManagementStatusCache(null!, new NoOpProxySessionAuditService(), NullLogger<OpenVpnManagementStatusCache>.Instance),
            new NoOpProxySessionAuditService(),
            NullLogger<ProxyByteDebugService>.Instance);

        var service = new ProxyByteDebugMonitorService(
            Options.Create(new OpenVpnProxyOptions { ByteDebug = true, ByteDebugIntervalSeconds = 1 }),
            active,
            flow,
            byteDebug,
            NullLogger<ProxyByteDebugMonitorService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(1300);
        await service.StopAsync(CancellationToken.None);
    }

    private static ProxyByteDebugMonitorService CreateService(OpenVpnProxyOptions options) =>
        new(
            Options.Create(options),
            new ActiveProxyConnectionService(),
            new ProxyTrafficFlowService(),
            new ProxyByteDebugService(
                Options.Create(options),
                new OpenVpnManagementStatusCache(null!, new NoOpProxySessionAuditService(), NullLogger<OpenVpnManagementStatusCache>.Instance),
                new NoOpProxySessionAuditService(),
                NullLogger<ProxyByteDebugService>.Instance),
            NullLogger<ProxyByteDebugMonitorService>.Instance);
}
