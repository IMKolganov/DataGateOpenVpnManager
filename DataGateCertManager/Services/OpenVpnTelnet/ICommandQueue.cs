namespace DataGateCertManager.Services.OpenVpnTelnet;

public interface ICommandQueue
{
    bool HasSubscribers { get; }
    void Subscribe(IMessageSubscriber subscriber);
    void Unsubscribe(IMessageSubscriber subscriber, string ip, int port);
    Task<string> SendCommandAsync(string command, CancellationToken cancellationToken, int timeoutMs = 5000);
    (bool result, string? message) TryGetMessage();
    Task DisconnectAsync();
}