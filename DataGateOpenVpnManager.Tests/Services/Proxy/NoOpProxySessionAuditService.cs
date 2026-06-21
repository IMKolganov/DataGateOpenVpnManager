namespace DataGateOpenVpnManager.Tests.Services.Proxy;

public sealed class NoOpProxySessionAuditService : DataGateOpenVpnManager.Services.Proxy.IProxySessionAuditService
{
    public void Record(DataGateOpenVpnManager.Services.Proxy.ProxySessionAuditEntry entry)
    {
    }

    public IReadOnlyList<DataGateOpenVpnManager.Services.Proxy.ProxySessionAuditEntry> GetRecent(int limit) =>
        Array.Empty<DataGateOpenVpnManager.Services.Proxy.ProxySessionAuditEntry>();
}
