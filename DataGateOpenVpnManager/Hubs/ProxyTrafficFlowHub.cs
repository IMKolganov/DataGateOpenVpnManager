using Microsoft.AspNetCore.SignalR;

namespace DataGateOpenVpnManager.Hubs;

public class ProxyTrafficFlowHub(ILogger<ProxyTrafficFlowHub> logger) : Hub
{
    public override Task OnConnectedAsync()
    {
        logger.LogInformation("ProxyTrafficFlowHub connected: {ConnectionId}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        logger.LogInformation("ProxyTrafficFlowHub disconnected: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public Task Ping(string? instanceId = null)
    {
        logger.LogDebug("Heartbeat from ProxyTrafficFlowHub client {Id} (instance={Instance})",
            Context.ConnectionId, instanceId ?? "n/a");
        return Task.CompletedTask;
    }
}
