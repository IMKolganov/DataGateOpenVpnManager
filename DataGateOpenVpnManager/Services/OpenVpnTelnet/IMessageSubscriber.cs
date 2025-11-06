namespace DataGateOpenVpnManager.Services.OpenVpnTelnet;

public interface IMessageSubscriber
{
    Task OnMessageReceived(string message, CancellationToken cancellationToken);
}