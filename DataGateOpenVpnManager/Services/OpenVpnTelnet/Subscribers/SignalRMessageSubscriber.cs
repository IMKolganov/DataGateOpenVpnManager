using DataGateOpenVpnManager.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace DataGateOpenVpnManager.Services.OpenVpnTelnet.Subscribers;

public class SignalRMessageSubscriber(IHubContext<OpenVpnSignalHub> hubContext, string connectionId)
    : IMessageSubscriber
{
    public async Task OnMessageReceived(string message, CancellationToken cancellationToken)
    {
        await hubContext.Clients.Client(connectionId).SendAsync("ReceiveMessage", message, cancellationToken);
    }
}