using DataGateOpenVpnManager.Models;
using Microsoft.Extensions.Options;

namespace DataGateOpenVpnManager.Services.Proxy;

public sealed class OpenVpnManagementStatusRefreshService(
    IOptions<OpenVpnProxyOptions> options,
    IOpenVpnManagementStatusCache statusCache,
    ILogger<OpenVpnManagementStatusRefreshService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!options.Value.NeedsBackgroundManagementRefresh)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            var interval = Math.Max(5, options.Value.ManagementStatusRefreshSeconds);
            try
            {
                await statusCache.RefreshAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[ManagementStatusCache] refresh loop failed");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
