using System.Collections.Concurrent;
using DataGateOpenVpnManager.Models.Proxy;

namespace DataGateOpenVpnManager.Services.Proxy;

public sealed class ActiveProxyConnectionService : IActiveProxyConnectionService
{
    private readonly ConcurrentDictionary<string, ActiveProxyConnection> _connections = new();

    public int Count => _connections.Count;

    public void Add(ActiveProxyConnection connection)
    {
        _connections[connection.ConnectionId] = connection;
    }

    public bool Remove(string connectionId)
    {
        return _connections.TryRemove(connectionId, out _);
    }

    public bool TryGet(string connectionId, out ActiveProxyConnection? connection)
    {
        var ok = _connections.TryGetValue(connectionId, out var value);
        connection = value;
        return ok;
    }

    public IReadOnlyCollection<ActiveProxyConnection> GetAll()
    {
        return _connections.Values.ToArray();
    }
}