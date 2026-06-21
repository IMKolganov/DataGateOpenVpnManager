using DataGateOpenVpnManager.Models;
using DataGateOpenVpnManager.Services.OpenVpnTelnet;
using DataGateOpenVpnManager.Services.Proxy;
using DataGateOpenVpnManager.Tests.Services.OpenVpnTelnet.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace DataGateOpenVpnManager.Tests.Services.Proxy;

public class OpenVpnManagementStatusCacheConcurrencyTests
{
    [Fact]
    public async Task RefreshAsync_ConcurrentCalls_ExecutesSingleManagementQueryAtATime()
    {
        var telnet = new ConcurrentTrackingFakeTelnetClient();
        var management = new OpenVpnManagementSignalService(telnet, Mock.Of<ILogger<CommandQueue>>());
        var cache = new OpenVpnManagementStatusCache(management, new NoOpProxySessionAuditService(), Mock.Of<ILogger<OpenVpnManagementStatusCache>>());
        const int refreshCount = 12;

        var responder = Task.Run(async () =>
        {
            for (var i = 0; i < refreshCount; i++)
            {
                while (telnet.SentCommands.Count <= i)
                    await Task.Delay(1);

                telnet.SimulateIncomingData(
                    "CLIENT_LIST\tadg-77\t127.0.0.1:55059\t10.51.16.3\t\t1000\t2000\t1\tUNDEF\nEND");
            }
        });

        var refreshTasks = Enumerable.Range(0, refreshCount)
            .Select(_ => cache.RefreshAsync(CancellationToken.None))
            .ToArray();

        await Task.WhenAll(refreshTasks);
        await responder;

        Assert.Equal(1, telnet.MaxConcurrentSends);
        Assert.Equal(refreshCount, telnet.SentCommands.Count);

        var snapshot = cache.GetSnapshot();
        Assert.NotNull(snapshot);
        Assert.True(snapshot!.IsValid);
        Assert.Single(snapshot.Clients);
        Assert.Equal("adg-77", snapshot.Clients[0].CommonName);
    }

    [Fact]
    public async Task ByteDebugAndZombieMonitors_ReadSameCachedSnapshot_WithoutDirectManagementCalls()
    {
        var telnet = new ConcurrentTrackingFakeTelnetClient();
        var management = new OpenVpnManagementSignalService(telnet, Mock.Of<ILogger<CommandQueue>>());
        var cache = new OpenVpnManagementStatusCache(management, new NoOpProxySessionAuditService(), Mock.Of<ILogger<OpenVpnManagementStatusCache>>());

        var refreshTask = cache.RefreshAsync(CancellationToken.None);
        await Task.Delay(20);
        telnet.SimulateIncomingData("CLIENT_LIST\tadg-77\t127.0.0.1:60123\t10.51.16.3\t\t1000\t2000\t1\tUNDEF\nEND");
        await refreshTask;

        telnet.SentCommands.Clear();

        var active = new ActiveProxyConnectionService();
        active.Add(new DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy.ActiveProxyConnection
        {
            ConnectionId = "conn-1",
            Protocol = DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy.Enums.ProxyConnectionProtocol.Udp,
            RealClientIp = "203.0.113.1",
            RealClientPort = 50000,
            LocalProxyIp = "127.0.0.1",
            LocalProxyPort = 60123,
            TargetIp = "127.0.0.1",
            TargetPort = 1194,
            ConnectedAtUtc = DateTime.UtcNow
        });

        var byteDebug = new ProxyByteDebugService(
            Options.Create(new OpenVpnProxyOptions { ByteDebug = true }),
            cache,
            new NoOpProxySessionAuditService(),
            Mock.Of<ILogger<ProxyByteDebugService>>());

        var zombieMonitor = new ProxyZombieConnectionMonitorService(
            Options.Create(new OpenVpnProxyOptions { CloseZombieAfterMissingSeconds = 90 }),
            active,
            new ProxyConnectionLifetimeService(new NoOpProxySessionAuditService(), Mock.Of<ILogger<ProxyConnectionLifetimeService>>()),
            cache,
            new NoOpProxySessionAuditService(),
            Mock.Of<ILogger<ProxyZombieConnectionMonitorService>>());

        await byteDebug.CompareAndLogAsync(new ProxyTrafficFlowUpdate
        {
            ConnectionId = "conn-1",
            Protocol = DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy.Enums.ProxyConnectionProtocol.Udp,
            State = "connected",
            IsConnected = true,
            IsIdle = false,
            LocalProxyIp = "127.0.0.1",
            LocalProxyPort = 60123,
            ClientToServerBytesTotal = 1000,
            ServerToClientBytesTotal = 2000,
            ConnectedAtUtc = DateTime.UtcNow,
            LastActivityAtUtc = DateTime.UtcNow,
            EmittedAtUtc = DateTime.UtcNow
        }, "periodic", CancellationToken.None);

        zombieMonitor.CheckActiveConnections();

        Assert.Empty(telnet.SentCommands);
    }

    private sealed class ConcurrentTrackingFakeTelnetClient : FakeTelnetClient
    {
        private int _concurrentSends;

        public int MaxConcurrentSends { get; private set; }

        public override Task SendAsync(string command, CancellationToken cancellationToken)
        {
            var inFlight = Interlocked.Increment(ref _concurrentSends);
            MaxConcurrentSends = Math.Max(MaxConcurrentSends, inFlight);
            try
            {
                return base.SendAsync(command, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _concurrentSends);
            }
        }
    }
}
