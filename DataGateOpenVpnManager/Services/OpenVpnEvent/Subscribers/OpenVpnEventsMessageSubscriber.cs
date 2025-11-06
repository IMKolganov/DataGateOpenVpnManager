using DataGateOpenVpnManager.Hubs;
using DataGateOpenVpnManager.Services.OpenVpnTelnet;
using Microsoft.AspNetCore.SignalR;

namespace DataGateOpenVpnManager.Services.OpenVpnEvent.Subscribers;

public class OpenVpnEventsMessageSubscriber(IHubContext<OpenVpnEventHub> hubContext, string connectionId)
    : IMessageSubscriber
{
    public async Task OnMessageReceived(string message, CancellationToken cancellationToken)
    {
        await hubContext.Clients.Client(connectionId).SendAsync("ReceiveEventMessage", message, cancellationToken);
    }
}