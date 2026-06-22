using DataGateOpenVpnManager.Hubs;
using DataGateOpenVpnManager.Models;
using DataGateOpenVpnManager.Services.Proxy;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.PiHole.Requests;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace DataGateOpenVpnManager.Services.PiHole;

public sealed class PiHoleQueryCollectorHostedService(
    IPiHoleRuntimeOptionsStore runtimeOptions,
    IPiHoleApiClient piHoleApiClient,
    IPiHoleClientIdentityResolver identityResolver,
    IPiHoleQueryCursorStore cursorStore,
    IOpenVpnManagementStatusCache managementStatusCache,
    IHubContext<OpenVpnEventHub> eventHub,
    ILogger<PiHoleQueryCollectorHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cfg = runtimeOptions.GetEffective();
        if (!cfg.Enabled)
        {
            logger.LogInformation("Pi-hole DNS query collector disabled (PiHole:Enabled=false).");
            return;
        }

        if (string.IsNullOrWhiteSpace(cfg.BaseUrl))
        {
            logger.LogWarning("Pi-hole DNS query collector disabled: PiHole:BaseUrl is empty.");
            return;
        }

        logger.LogInformation(
            "Pi-hole DNS query collector started. BaseUrl={BaseUrl}, Interval={Interval}s, BatchSize={BatchSize}",
            cfg.BaseUrl, cfg.PollIntervalSeconds, cfg.BatchSize);

        var interval = TimeSpan.FromSeconds(Math.Max(10, cfg.PollIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CollectOnceAsync(cfg, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Pi-hole DNS query collection failed.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    internal async Task<int> CollectOnceAsync(PiHoleOptions cfg, CancellationToken cancellationToken)
    {
        var untilUtc = DateTimeOffset.UtcNow;
        var lastUntil = cursorStore.GetLastUntilUtc();
        var fromUtc = lastUntil?.AddSeconds(-Math.Max(0, cfg.LookbackSeconds))
                      ?? untilUtc.AddSeconds(-Math.Max(cfg.LookbackSeconds, cfg.PollIntervalSeconds));

        if (fromUtc >= untilUtc)
            fromUtc = untilUtc.AddSeconds(-5);

        var records = await piHoleApiClient.GetQueriesSinceAsync(
            fromUtc,
            untilUtc,
            cfg.BatchSize,
            cancellationToken);
        if (records.Count == 0)
        {
            cursorStore.SaveLastUntilUtc(untilUtc);
            return 0;
        }

        var snapshot = managementStatusCache.GetSnapshot();
        if (snapshot is null || !snapshot.IsValid)
        {
            await managementStatusCache.RefreshAsync(cancellationToken);
            snapshot = managementStatusCache.GetSnapshot();
        }

        var enriched = identityResolver.Enrich(records, snapshot);
        if (enriched.Count == 0)
        {
            cursorStore.SaveLastUntilUtc(untilUtc);
            return 0;
        }

        var batch = new DnsQueryBatchRequest
        {
            CollectedAtUtc = DateTimeOffset.UtcNow,
            Queries = enriched
        };

        await eventHub.Clients.All.SendAsync("DnsQueriesReceived", batch, cancellationToken);
        cursorStore.SaveLastUntilUtc(untilUtc);
        logger.LogInformation("Forwarded {Count} Pi-hole DNS queries to event hub.", enriched.Count);
        return enriched.Count;
    }
}
