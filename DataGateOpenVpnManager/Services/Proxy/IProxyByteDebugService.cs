namespace DataGateOpenVpnManager.Services.Proxy;

public interface IProxyByteDebugService
{
    void ReportDisconnect(ProxyTrafficFlowUpdate update);
}
