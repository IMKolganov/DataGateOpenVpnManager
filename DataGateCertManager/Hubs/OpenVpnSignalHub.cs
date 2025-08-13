using DataGateCertManager.Services.OpenVpnTelnet;
using DataGateCertManager.Services.OpenVpnTelnet.Subscribers;
using Microsoft.AspNetCore.SignalR;

namespace DataGateCertManager.Hubs;

public class OpenVpnSignalHub(OpenVpnManagementSignalService vpnService, ILogger<OpenVpnSignalHub> logger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        logger.LogInformation("New client connected: {ConnectionId}, RemoteIP={RemoteIpAddress}",
            Context.ConnectionId,
            Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString());

        var subscriber = new SignalRMessageSubscriber(
            Context.GetHttpContext()!.RequestServices.GetRequiredService<IHubContext<OpenVpnSignalHub>>(),
            Context.ConnectionId
        );

        vpnService.Subscribe(subscriber);
        logger.LogInformation("Subscriber registered for ConnectionId={ConnectionId}", Context.ConnectionId);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception != null)
        {
            logger.LogWarning(exception, "Client disconnected with error: {ConnectionId}", Context.ConnectionId);
        }
        else
        {
            logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task SendCommand(string command)
    {
        logger.LogInformation("Received SendCommand from {ConnectionId}: {Command}", Context.ConnectionId, command);

        var result = await vpnService.SendCommandAsync(command, Context.ConnectionAborted);

        logger.LogInformation("Sending result to all clients: {Result}", result);
        await Clients.All.SendAsync("ReceiveCommandResult", result);
    }
    
    public async Task SendCommandWithRequestId(string requestId, string command)
    {
        logger.LogInformation(
            "Received SendCommandWithRequestId from {ConnectionId}, RequestId={RequestId}: {Command}",
            Context.ConnectionId, requestId, command);

        var result = await vpnService.SendCommandAsync(command, Context.ConnectionAborted);

        logger.LogInformation("Sending result to caller {ConnectionId} for RequestId={RequestId}: {Result}",
            Context.ConnectionId, requestId, result);

        await Clients.Caller.SendAsync("ReceiveCommandResultWithRequestId", requestId, result);
    }
}
