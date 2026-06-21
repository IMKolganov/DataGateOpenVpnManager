using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy;
using DataGateOpenVpnManager.Models;
using Microsoft.Extensions.Options;

namespace DataGateOpenVpnManager.Services.Proxy;

public sealed class ProxyByteDebugMonitorService(
    IOptions<OpenVpnProxyOptions> options,
    IActiveProxyConnectionService activeConnections,
    IProxyTrafficFlowService trafficFlow,
    ProxyByteDebugService byteDebug,
    ILogger<ProxyByteDebugMonitorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var opts = options.Value;
            if (!opts.ByteDebug || opts.ByteDebugIntervalSeconds <= 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(opts.ByteDebugIntervalSeconds), stoppingToken);
                await SnapshotActiveConnectionsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[ProxyByteDebug] periodic snapshot failed");
            }
        }
    }

    private async Task SnapshotActiveConnectionsAsync(CancellationToken cancellationToken)
    {
        var connections = activeConnections.GetAll();
        if (connections.Count == 0)
            return;

        foreach (var connection in connections)
        {
            if (!trafficFlow.TryGetTotals(connection.ConnectionId, out var c2s, out var s2c))
                continue;

            var update = BuildSnapshotUpdate(connection, c2s, s2c);
            await byteDebug.CompareAndLogAsync(update, "periodic", cancellationToken);
        }
    }

    private static ProxyTrafficFlowUpdate BuildSnapshotUpdate(ActiveProxyConnection connection, long c2s, long s2c) =>
        new()
        {
            ConnectionId = connection.ConnectionId,
            Protocol = connection.Protocol,
            State = "connected",
            IsConnected = true,
            IsIdle = false,
            RealClientIp = connection.RealClientIp,
            RealClientPort = connection.RealClientPort,
            LocalProxyIp = connection.LocalProxyIp,
            LocalProxyPort = connection.LocalProxyPort,
            TargetIp = connection.TargetIp,
            TargetPort = connection.TargetPort,
            ClientToServerBytesTotal = c2s,
            ServerToClientBytesTotal = s2c,
            ConnectedAtUtc = connection.ConnectedAtUtc,
            LastActivityAtUtc = DateTime.UtcNow,
            EmittedAtUtc = DateTime.UtcNow
        };
}
