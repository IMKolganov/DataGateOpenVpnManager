using System.Collections.Concurrent;
using System.Globalization;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy;
using DataGateOpenVpnManager.Models;
using Microsoft.Extensions.Options;

namespace DataGateOpenVpnManager.Services.Proxy;

public sealed class ProxyZombieConnectionMonitorService(
    IOptions<OpenVpnProxyOptions> options,
    IActiveProxyConnectionService activeConnections,
    IProxyConnectionLifetimeService connectionLifetime,
    IOpenVpnManagementStatusCache statusCache,
    IProxySessionAuditService audit,
    ILogger<ProxyZombieConnectionMonitorService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, DateTime> _missingSinceUtc = new();
    private readonly ConcurrentDictionary<string, int> _consecutiveMisses = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var checkInterval = Math.Max(5, options.Value.ZombieCheckIntervalSeconds);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(checkInterval), stoppingToken);
                CheckActiveConnections();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[ProxyZombie] health check failed");
            }
        }
    }

    internal void CheckActiveConnections()
    {
        var opts = options.Value;
        if (opts.CloseZombieAfterMissingSeconds <= 0)
            return;

        var connections = activeConnections.GetAll();
        if (connections.Count == 0)
        {
            _missingSinceUtc.Clear();
            _consecutiveMisses.Clear();
            return;
        }

        var snapshot = statusCache.GetSnapshot();
        if (!ProxyManagementPeerDiagnostics.CanEvaluatePeerPresence(snapshot, opts, out var skipReason))
        {
            Audit(null, "zombie.check_skipped", "skip", skipReason ?? "peer evaluation unavailable",
                ProxyAuditDetails.ForSnapshot(snapshot));
            return;
        }

        var now = DateTime.UtcNow;
        var cacheAge = now - snapshot!.FetchedAtUtc;
        var activeIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var connection in connections)
        {
            activeIds.Add(connection.ConnectionId);
            var details = ProxyAuditDetails.ForConnection(connection);
            foreach (var kv in ProxyAuditDetails.ForSnapshot(snapshot))
                details[kv.Key] = kv.Value;

            if (snapshot.FetchedAtUtc < connection.ConnectedAtUtc)
            {
                _missingSinceUtc.TryRemove(connection.ConnectionId, out _);
                _consecutiveMisses.TryRemove(connection.ConnectionId, out _);
                Audit(connection.ConnectionId, "zombie.peer_check", "skip", "cache older than connection", details);
                continue;
            }

            var mgmtClient = OpenVpnManagementStatusParser.FindByLocalProxyPort(
                snapshot.Clients,
                connection.LocalProxyIp,
                connection.LocalProxyPort);

            if (mgmtClient is not null)
            {
                _missingSinceUtc.TryRemove(connection.ConnectionId, out _);
                _consecutiveMisses.TryRemove(connection.ConnectionId, out _);
                details["cn"] = mgmtClient.CommonName;
                details["virt"] = mgmtClient.VirtualAddress;
                Audit(connection.ConnectionId, "zombie.peer_check", "ok", "peer in management", details);
                continue;
            }

            var missingSince = _missingSinceUtc.GetOrAdd(connection.ConnectionId, now);
            var missingFor = now - missingSince;
            var misses = _consecutiveMisses.AddOrUpdate(connection.ConnectionId, 1, (_, current) => current + 1);
            details["missingForSec"] = missingFor.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture);
            details["consecutiveMisses"] = misses.ToString(CultureInfo.InvariantCulture);
            details["requiredMisses"] = opts.ZombieMinConsecutiveMisses.ToString(CultureInfo.InvariantCulture);
            details["closeAfterSec"] = opts.CloseZombieAfterMissingSeconds.ToString(CultureInfo.InvariantCulture);

            logger.LogWarning(
                "[ProxyZombie] OpenVPN peer missing connectionId={ConnectionId} proto={Protocol} local={LocalIp}:{LocalPort} client={RealClientIp}:{RealClientPort} missingForSec={MissingForSec:F0} consecutiveMisses={Misses} cacheAgeSec={CacheAgeSec:F0}",
                connection.ConnectionId,
                connection.Protocol,
                connection.LocalProxyIp,
                connection.LocalProxyPort,
                connection.RealClientIp,
                connection.RealClientPort,
                missingFor.TotalSeconds,
                misses,
                cacheAge.TotalSeconds);

            if (misses < opts.ZombieMinConsecutiveMisses)
            {
                Audit(connection.ConnectionId, "zombie.peer_missing", "wait", "consecutive misses below threshold", details);
                continue;
            }

            if (missingFor < TimeSpan.FromSeconds(opts.CloseZombieAfterMissingSeconds))
            {
                Audit(connection.ConnectionId, "zombie.peer_missing", "wait", "missing duration below threshold", details);
                continue;
            }

            Audit(connection.ConnectionId, "zombie.peer_missing", "terminate_requested", "thresholds met", details);
            if (connectionLifetime.TryTerminate(
                    connection.ConnectionId,
                    $"OpenVPN peer missing from management for {missingFor.TotalSeconds:F0}s ({misses} checks)"))
            {
                _missingSinceUtc.TryRemove(connection.ConnectionId, out _);
                _consecutiveMisses.TryRemove(connection.ConnectionId, out _);
            }
        }

        foreach (var staleId in _missingSinceUtc.Keys.Where(id => !activeIds.Contains(id)).ToArray())
        {
            _missingSinceUtc.TryRemove(staleId, out _);
            _consecutiveMisses.TryRemove(staleId, out _);
        }
    }

    private void Audit(
        string? connectionId,
        string eventName,
        string decision,
        string reason,
        Dictionary<string, string>? details = null)
    {
        audit.Record(new ProxySessionAuditEntry
        {
            AtUtc = DateTime.UtcNow,
            Event = eventName,
            ConnectionId = connectionId,
            Decision = decision,
            Reason = reason,
            Details = details
        });
    }
}
