using System.Collections.Concurrent;
using System.Net;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy;

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

    public ActiveProxyConnection? TryGetByLocalProxy(int localProxyPort, string? host)
    {
        var needle = NormalizeHost(host);
        foreach (var c in _connections.Values)
        {
            if (c.LocalProxyPort != localProxyPort)
                continue;
            if (HostsEqual(c.LocalProxyIp, needle))
                return c;
        }

        return null;
    }

    private static bool HostsEqual(string? localProxyIp, string needleNormalized)
    {
        return string.Equals(NormalizeHost(localProxyIp), needleNormalized, StringComparison.OrdinalIgnoreCase);
    }

    internal static string NormalizeHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return NormalizeHost("127.0.0.1");

        var h = host.Trim();
        if (h.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return "127.0.0.1";

        if (!IPAddress.TryParse(h, out var ip))
            return h;

        if (IPAddress.IsLoopback(ip))
            return "127.0.0.1";

        return ip.ToString();
    }

    public IReadOnlyCollection<ActiveProxyConnection> GetAll()
    {
        return _connections.Values.ToArray();
    }
}