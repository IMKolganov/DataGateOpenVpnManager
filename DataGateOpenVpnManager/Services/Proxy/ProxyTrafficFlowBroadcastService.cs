using DataGateOpenVpnManager.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace DataGateOpenVpnManager.Services.Proxy;

public sealed class ProxyTrafficFlowBroadcastService(
    IProxyTrafficFlowService flowService,
    IHubContext<ProxyTrafficFlowHub> hubContext,
    ILogger<ProxyTrafficFlowBroadcastService> logger) : BackgroundService
{
    private static readonly TimeSpan BroadcastInterval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(BroadcastInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    break;

                var batch = flowService.BuildBatch(DateTime.UtcNow);
                if (batch.Count == 0)
                    continue;
                await hubContext.Clients.All.SendAsync("TrafficFlowUpdated", batch, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to broadcast proxy traffic flow batch.");
            }
        }
    }
}
