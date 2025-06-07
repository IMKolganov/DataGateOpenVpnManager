using DataGateCertManager.Hubs;
using DataGateCertManager.Services.OpenVpnTelnet;
using Microsoft.AspNetCore.SignalR;

namespace DataGateCertManager.Services.OpenVpnEvent.Subscribers;

public class OpenVpnEventsMessageSubscriber(IHubContext<OpenVpnEventHub> hubContext, string connectionId)
    : IMessageSubscriber
{
    public async Task OnMessageReceived(string message, CancellationToken cancellationToken)
    {
        await hubContext.Clients.Client(connectionId).SendAsync("ReceiveEventMessage", message, cancellationToken);
    }
}