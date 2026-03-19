using DataGateOpenVpnManager.Models.Proxy;

namespace DataGateOpenVpnManager.Services.Proxy;

public interface IActiveProxyConnectionService
{
    void Add(ActiveProxyConnection connection);
    bool Remove(string connectionId);
    bool TryGet(string connectionId, out ActiveProxyConnection? connection);
    IReadOnlyCollection<ActiveProxyConnection> GetAll();
    int Count { get; }
}