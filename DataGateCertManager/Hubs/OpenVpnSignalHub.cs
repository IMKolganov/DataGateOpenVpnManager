using System.Collections.Concurrent;
using DataGateCertManager.Services.OpenVpnTelnet;
using DataGateCertManager.Services.OpenVpnTelnet.Subscribers;
using Microsoft.AspNetCore.SignalR;

namespace DataGateCertManager.Hubs;

public class OpenVpnSignalHub(
    OpenVpnManagementSignalService vpnService,
    ILogger<OpenVpnSignalHub> logger) : Hub
{
    // Keeps per-connection subscribers so we can unsubscribe on disconnect
    private static readonly ConcurrentDictionary<string, SignalRMessageSubscriber> _subscribers = new();

    public override async Task OnConnectedAsync()
    {
        var remoteIp = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString();
        logger.LogInformation("OpenVpnSignalHub connected: {ConnectionId}, RemoteIP={RemoteIp}", Context.ConnectionId, remoteIp);

        // Create per-connection subscriber and subscribe to vpnService
        var hubCtx = Context.GetHttpContext()!.RequestServices.GetRequiredService<IHubContext<OpenVpnSignalHub>>();
        var subscriber = new SignalRMessageSubscriber(hubCtx, Context.ConnectionId);

        if (_subscribers.TryAdd(Context.ConnectionId, subscriber))
        {
            vpnService.Subscribe(subscriber);
            logger.LogInformation("Subscriber registered for ConnectionId={ConnectionId}. Total={Total}",
                Context.ConnectionId, _subscribers.Count);
        }
        else
        {
            logger.LogWarning("Subscriber already exists for ConnectionId={ConnectionId}", Context.ConnectionId);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (_subscribers.TryRemove(Context.ConnectionId, out var subscriber))
        {
            try
            {
                // If your vpnService has Unsubscribe, call it here
                vpnService.Unsubscribe(subscriber, Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString() ?? "unknown", 0);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Unsubscribe failed for ConnectionId={ConnectionId}", Context.ConnectionId);
            }

            logger.LogInformation("OpenVpnSignalHub disconnected: {ConnectionId}. Total={Total}", Context.ConnectionId, _subscribers.Count);
        }
        else
        {
            logger.LogInformation("OpenVpnSignalHub disconnected (no subscriber found): {ConnectionId}", Context.ConnectionId);
        }

        if (exception != null)
            logger.LogWarning(exception, "Disconnect error for {ConnectionId}", Context.ConnectionId);

        await base.OnDisconnectedAsync(exception);
    }

    // Client heartbeat to monitor liveness (call periodically from clients)
    public Task Ping(string? instanceId = null)
    {
        logger.LogDebug("Heartbeat from {ConnectionId}, instance={InstanceId}", Context.ConnectionId, instanceId ?? "n/a");
        return Task.CompletedTask;
    }

    public async Task SendCommand(string command)
    {
        logger.LogInformation("SendCommand from {ConnectionId}: {Command}", Context.ConnectionId, command);
        try
        {
            var result = await vpnService.SendCommandAsync(command, Context.ConnectionAborted);
            await Clients.All.SendAsync("ReceiveCommandResult", result);
            logger.LogInformation("Result broadcasted to all. Length={Len}", result?.Length ?? 0);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SendCommand failed for {ConnectionId}", Context.ConnectionId);
            throw;
        }
    }

    public async Task SendCommandWithRequestId(string requestId, string command)
    {
        logger.LogInformation("SendCommandWithRequestId from {ConnectionId}, RequestId={RequestId}: {Command}",
            Context.ConnectionId, requestId, command);
        try
        {
            var result = await vpnService.SendCommandAsync(command, Context.ConnectionAborted);
            await Clients.Caller.SendAsync("ReceiveCommandResultWithRequestId", requestId, result);
            logger.LogInformation("Result sent to caller for RequestId={RequestId}. Length={Len}", requestId, result?.Length ?? 0);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SendCommandWithRequestId failed for {ConnectionId}, RequestId={RequestId}",
                Context.ConnectionId, requestId);
            throw;
        }
    }
}
