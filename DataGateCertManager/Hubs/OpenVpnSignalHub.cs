using DataGateCertManager.Services.OpenVpnTelnet;
using DataGateCertManager.Services.OpenVpnTelnet.Subscribers;
using Microsoft.AspNetCore.SignalR;

namespace DataGateCertManager.Hubs;

public class OpenVpnSignalHub(OpenVpnManagementSignalService vpnService, ILogger<OpenVpnSignalHub> logger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var subscriber = new SignalRMessageSubscriber(
            Context.GetHttpContext()!.RequestServices.GetRequiredService<IHubContext<OpenVpnSignalHub>>(),
            Context.ConnectionId
        );
        vpnService.Subscribe(subscriber);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task SendCommand(string command)
    {
        var result = await vpnService.SendCommandAsync(command, Context.ConnectionAborted);

        await Clients.All.SendAsync("ReceiveCommandResult", result);
    }
}