using System.Collections.Concurrent;

namespace DataGateOpenVpnManager.Services.Proxy;

public sealed class ProxyConnectionLifetimeService(
    IProxySessionAuditService audit,
    ILogger<ProxyConnectionLifetimeService> logger) : IProxyConnectionLifetimeService
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _connections = new();

    public void Register(string connectionId, CancellationTokenSource cancellation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(cancellation);
        _connections[connectionId] = cancellation;
    }

    public void Unregister(string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
            return;

        _connections.TryRemove(connectionId, out _);
    }

    public bool TryTerminate(string connectionId, string reason)
    {
        if (!_connections.TryRemove(connectionId, out var cancellation))
            return false;

        if (cancellation.IsCancellationRequested)
            return true;

        logger.LogWarning(
            "[ProxyZombie] terminating connectionId={ConnectionId} reason={Reason}",
            connectionId,
            reason);

        audit.Record(new ProxySessionAuditEntry
        {
            AtUtc = DateTime.UtcNow,
            Event = "proxy.terminated",
            ConnectionId = connectionId,
            Decision = "terminate",
            Reason = reason
        });

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // already torn down
        }

        return true;
    }
}
