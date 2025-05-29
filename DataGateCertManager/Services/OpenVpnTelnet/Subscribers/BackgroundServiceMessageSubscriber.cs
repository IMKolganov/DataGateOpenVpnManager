namespace DataGateCertManager.Services.OpenVpnTelnet.Subscribers;

public class BackgroundServiceMessageSubscriber : IMessageSubscriber
{
    public BackgroundServiceMessageSubscriber()
    {
    }

    public Task OnMessageReceived(string message, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
