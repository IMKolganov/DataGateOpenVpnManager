using DataGateOpenVpnManager.Models.Proxy;

namespace DataGateOpenVpnManager.Services.Proxy;


public interface IProxyConnectionHistoryService
{
    void Add(ProxyConnectionHistoryItem item);
    IReadOnlyCollection<ProxyConnectionHistoryItem> GetAll();
    int Count { get; }
}