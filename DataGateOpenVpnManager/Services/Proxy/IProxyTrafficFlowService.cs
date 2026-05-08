using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy.Enums;

namespace DataGateOpenVpnManager.Services.Proxy;

public interface IProxyTrafficFlowService
{
    void RegisterConnection(ActiveProxyConnection connection, ProxyConnectionIdentity? identity = null);
    void UnregisterConnection(string connectionId, DateTime? disconnectedAtUtc = null);
    void RegisterConnectFailed(
        string connectionId,
        ProxyConnectionProtocol protocol,
        string? realClientIp,
        int realClientPort,
        ProxyConnectionIdentity? identity,
        string targetIp,
        int targetPort,
        string? errorMessage,
        DateTime? failedAtUtc = null);
    void RecordTraffic(
        string connectionId,
        ProxyTrafficFlowDirection direction,
        int bytes,
        DateTime? occurredAtUtc = null);
    IReadOnlyCollection<ProxyTrafficFlowUpdate> BuildBatch(DateTime emittedAtUtc);
}
