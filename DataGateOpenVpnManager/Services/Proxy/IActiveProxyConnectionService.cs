using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy;

namespace DataGateOpenVpnManager.Services.Proxy;

public interface IActiveProxyConnectionService
{
    void Add(ActiveProxyConnection connection);
    bool Remove(string connectionId);
    bool TryGet(string connectionId, out ActiveProxyConnection? connection);
    ActiveProxyConnection? TryGetByLocalProxy(int localProxyPort, string? host);
    IReadOnlyCollection<ActiveProxyConnection> GetAll();
    int Count { get; }
}