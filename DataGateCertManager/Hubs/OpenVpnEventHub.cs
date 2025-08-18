using Microsoft.AspNetCore.SignalR;

namespace DataGateCertManager.Hubs;

public class OpenVpnEventHub(HubConnectionTracker tracker, ILogger<OpenVpnEventHub> logger) : Hub
{
    public override Task OnConnectedAsync()
    {
        tracker.EventHubConnected(Context.ConnectionId);
        logger.LogInformation("OpenVpnEventHub connected: {Id}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        tracker.EventHubDisconnected(Context.ConnectionId);
        logger.LogInformation("OpenVpnEventHub disconnected: {Id}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    // Client heartbeat (client calls this periodically)
    public Task Ping(string? instanceId = null)
    {
        tracker.TouchHeartbeat();
        logger.LogDebug("Heartbeat from EventHub client {Id} (instance={Instance})",
            Context.ConnectionId, instanceId ?? "n/a");
        return Task.CompletedTask;
    }
}