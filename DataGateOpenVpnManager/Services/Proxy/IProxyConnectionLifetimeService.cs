namespace DataGateOpenVpnManager.Services.Proxy;

public interface IProxyConnectionLifetimeService
{
    void Register(string connectionId, CancellationTokenSource cancellation);
    void Unregister(string connectionId);
    bool TryTerminate(string connectionId, string reason);
}
