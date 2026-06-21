using DataGateOpenVpnManager.Models;
using DataGateOpenVpnManager.Services.OpenVpnTelnet;
using DataGateOpenVpnManager.Services.Proxy;
using DataGateOpenVpnManager.Tests.Services.OpenVpnTelnet.Fakes;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace DataGateOpenVpnManager.Tests.Services.Proxy;

public class ProxyZombieConnectionMonitorServiceTests
{
    [Fact]
    public async Task CheckActiveConnections_TerminatesConnection_AfterConsecutiveMissesAndDuration()
    {
        var active = new ActiveProxyConnectionService();
        active.Add(new ActiveProxyConnection
        {
            ConnectionId = "conn-zombie",
            Protocol = ProxyConnectionProtocol.Udp,
            RealClientIp = "203.0.113.1",
            RealClientPort = 50000,
            LocalProxyIp = "127.0.0.1",
            LocalProxyPort = 60123,
            TargetIp = "127.0.0.1",
            TargetPort = 1194,
            ConnectedAtUtc = DateTime.UtcNow.AddMinutes(-5)
        });

        using var cts = new CancellationTokenSource();
        var lifetime = new ProxyConnectionLifetimeService(new NoOpProxySessionAuditService(), Mock.Of<ILogger<ProxyConnectionLifetimeService>>());
        lifetime.Register("conn-zombie", cts);

        var telnet = new FakeTelnetClient();
        var management = new OpenVpnManagementSignalService(telnet, Mock.Of<ILogger<CommandQueue>>());
        var cache = new OpenVpnManagementStatusCache(management, new NoOpProxySessionAuditService(), Mock.Of<ILogger<OpenVpnManagementStatusCache>>());

        var monitor = new ProxyZombieConnectionMonitorService(
            Options.Create(new OpenVpnProxyOptions
            {
                CloseZombieAfterMissingSeconds = 60,
                ZombieMinConsecutiveMisses = 3,
                ZombieCheckIntervalSeconds = 30,
                ManagementStatusRefreshSeconds = 30
            }),
            active,
            lifetime,
            cache,
            new NoOpProxySessionAuditService(),
            Mock.Of<ILogger<ProxyZombieConnectionMonitorService>>());

        var refreshTask = cache.RefreshAsync(CancellationToken.None);
        telnet.SimulateIncomingData(
            "CLIENT_LIST\tother\t127.0.0.1:59999\t10.51.16.2\t\t10\t20\t1\tUNDEF\nEND");
        await refreshTask;

        var missingField = typeof(ProxyZombieConnectionMonitorService)
            .GetField("_missingSinceUtc", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var missingDict = (System.Collections.Concurrent.ConcurrentDictionary<string, DateTime>)missingField.GetValue(monitor)!;
        missingDict["conn-zombie"] = DateTime.UtcNow.AddSeconds(-120);

        monitor.CheckActiveConnections();
        monitor.CheckActiveConnections();
        monitor.CheckActiveConnections();

        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public async Task CheckActiveConnections_DoesNotTerminate_WhenClientListEmptyButProxyActive()
    {
        var active = new ActiveProxyConnectionService();
        active.Add(new ActiveProxyConnection
        {
            ConnectionId = "conn-live",
            Protocol = ProxyConnectionProtocol.Udp,
            RealClientIp = "203.0.113.1",
            RealClientPort = 50000,
            LocalProxyIp = "127.0.0.1",
            LocalProxyPort = 60123,
            TargetIp = "127.0.0.1",
            TargetPort = 1194,
            ConnectedAtUtc = DateTime.UtcNow.AddMinutes(-1)
        });

        using var cts = new CancellationTokenSource();
        var lifetime = new ProxyConnectionLifetimeService(new NoOpProxySessionAuditService(), Mock.Of<ILogger<ProxyConnectionLifetimeService>>());
        lifetime.Register("conn-live", cts);

        var telnet = new FakeTelnetClient();
        var management = new OpenVpnManagementSignalService(telnet, Mock.Of<ILogger<CommandQueue>>());
        var cache = new OpenVpnManagementStatusCache(management, new NoOpProxySessionAuditService(), Mock.Of<ILogger<OpenVpnManagementStatusCache>>());

        var monitor = new ProxyZombieConnectionMonitorService(
            Options.Create(new OpenVpnProxyOptions
            {
                CloseZombieAfterMissingSeconds = 30,
                ZombieMinConsecutiveMisses = 1,
                ManagementStatusRefreshSeconds = 30
            }),
            active,
            lifetime,
            cache,
            new NoOpProxySessionAuditService(),
            Mock.Of<ILogger<ProxyZombieConnectionMonitorService>>());

        var refreshTask = cache.RefreshAsync(CancellationToken.None);
        telnet.SimulateIncomingData("END");
        await refreshTask;

        monitor.CheckActiveConnections();

        Assert.False(cts.IsCancellationRequested);
    }

    [Fact]
    public void TryTerminate_CancelsRegisteredConnection()
    {
        using var cts = new CancellationTokenSource();
        var lifetime = new ProxyConnectionLifetimeService(new NoOpProxySessionAuditService(), Mock.Of<ILogger<ProxyConnectionLifetimeService>>());
        lifetime.Register("conn-1", cts);

        Assert.True(lifetime.TryTerminate("conn-1", "test"));
        Assert.True(cts.IsCancellationRequested);
        Assert.False(lifetime.TryTerminate("conn-1", "test"));
    }
}
