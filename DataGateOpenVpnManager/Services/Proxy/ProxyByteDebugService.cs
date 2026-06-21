using System.Globalization;
using DataGateOpenVpnManager.Models;
using Microsoft.Extensions.Options;

namespace DataGateOpenVpnManager.Services.Proxy;

public sealed class ProxyByteDebugService(
    IOptions<OpenVpnProxyOptions> options,
    IOpenVpnManagementStatusCache statusCache,
    IProxySessionAuditService audit,
    ILogger<ProxyByteDebugService> logger) : IProxyByteDebugService
{
    public void ReportDisconnect(ProxyTrafficFlowUpdate update)
    {
        if (!options.Value.ByteDebug)
            return;

        _ = CompareAndLogAsync(update, "disconnect", CancellationToken.None);
    }

    internal Task CompareAndLogAsync(ProxyTrafficFlowUpdate update, string reason, CancellationToken cancellationToken)
    {
        if (update.LocalProxyPort is < 1 or > 65535)
        {
            logger.LogInformation(
                "[ProxyByteDebug] {Reason}: skip connectionId={ConnectionId} — no local proxy port",
                reason,
                update.ConnectionId);
            return Task.CompletedTask;
        }

        return CompareAndLogCoreAsync(update, reason, cancellationToken);
    }

    private async Task CompareAndLogCoreAsync(
        ProxyTrafficFlowUpdate update,
        string reason,
        CancellationToken cancellationToken)
    {
        if (string.Equals(reason, "disconnect", StringComparison.Ordinal))
            await statusCache.RefreshAsync(cancellationToken);

        var snapshot = statusCache.GetSnapshot();
        if (snapshot is null || !snapshot.IsValid)
        {
            audit.Record(new ProxySessionAuditEntry
            {
                AtUtc = DateTime.UtcNow,
                Event = "byte_debug.skipped",
                ConnectionId = update.ConnectionId,
                Decision = "skip",
                Reason = $"{reason}: management cache empty"
            });
            return;
        }

        var mgmtClient = OpenVpnManagementStatusParser.FindByLocalProxyPort(
            snapshot.Clients,
            update.LocalProxyIp,
            update.LocalProxyPort);

        if (mgmtClient is null)
        {
            var missDetails = ProxyAuditDetails.ForSnapshot(snapshot);
            missDetails["proxyC2S"] = update.ClientToServerBytesTotal.ToString(CultureInfo.InvariantCulture);
            missDetails["proxyS2C"] = update.ServerToClientBytesTotal.ToString(CultureInfo.InvariantCulture);
            audit.Record(new ProxySessionAuditEntry
            {
                AtUtc = DateTime.UtcNow,
                Event = "byte_debug.compared",
                ConnectionId = update.ConnectionId,
                Decision = "peer_missing_in_cache",
                Reason = reason,
                Details = missDetails
            });
            logger.LogWarning(
                "[ProxyByteDebug] {Reason}: client not in cached management status connectionId={ConnectionId} local={LocalIp}:{LocalPort} proxyC2S={ProxyC2S} proxyS2C={ProxyS2C} cacheAgeSec={CacheAgeSec:F0}",
                reason,
                update.ConnectionId,
                update.LocalProxyIp,
                update.LocalProxyPort,
                update.ClientToServerBytesTotal,
                update.ServerToClientBytesTotal,
                (DateTime.UtcNow - snapshot.FetchedAtUtc).TotalSeconds);
            return;
        }

        var comparison = ProxyByteComparison.Create(
            update.ClientToServerBytesTotal,
            update.ServerToClientBytesTotal,
            mgmtClient.BytesReceived,
            mgmtClient.BytesSent);

        LogComparison(update, mgmtClient, comparison, reason, snapshot.FetchedAtUtc);

        var okDetails = ProxyAuditDetails.ForSnapshot(snapshot);
        okDetails["proxyC2S"] = update.ClientToServerBytesTotal.ToString(CultureInfo.InvariantCulture);
        okDetails["proxyS2C"] = update.ServerToClientBytesTotal.ToString(CultureInfo.InvariantCulture);
        okDetails["mgmtRecv"] = mgmtClient.BytesReceived.ToString(CultureInfo.InvariantCulture);
        okDetails["mgmtSent"] = mgmtClient.BytesSent.ToString(CultureInfo.InvariantCulture);
        okDetails["deltaC2S"] = comparison.DeltaClientToServer.ToString(CultureInfo.InvariantCulture);
        okDetails["deltaS2C"] = comparison.DeltaServerToClient.ToString(CultureInfo.InvariantCulture);
        okDetails["cn"] = mgmtClient.CommonName;
        audit.Record(new ProxySessionAuditEntry
        {
            AtUtc = DateTime.UtcNow,
            Event = "byte_debug.compared",
            ConnectionId = update.ConnectionId,
            Decision = "ok",
            Reason = reason,
            Details = okDetails
        });
    }

    private void LogComparison(
        ProxyTrafficFlowUpdate update,
        OpenVpnManagementClientEntry mgmtClient,
        ProxyByteComparison comparison,
        string reason,
        DateTime cacheFetchedAtUtc)
    {
        var warnDelta = options.Value.ByteDebugWarnDeltaBytes;
        var logLevel = comparison.HasMaterialDelta(warnDelta) ? LogLevel.Warning : LogLevel.Information;

        logger.Log(
            logLevel,
            "[ProxyByteDebug] {Reason} connectionId={ConnectionId} proto={Protocol} local={LocalIp}:{LocalPort} client={RealClientIp}:{RealClientPort} cn={CommonName} virt={VirtualAddress} " +
            "proxyC2S={ProxyC2S} proxyS2C={ProxyS2C} mgmtRecv={MgmtRecv} mgmtSent={MgmtSent} " +
            "deltaC2S={DeltaC2S} deltaS2C={DeltaS2C} cacheAgeSec={CacheAgeSec:F0} (proxy-mgmt; C2S↔recv, S2C↔sent)",
            reason,
            update.ConnectionId,
            update.Protocol,
            update.LocalProxyIp,
            update.LocalProxyPort,
            update.RealClientIp,
            update.RealClientPort,
            mgmtClient.CommonName,
            mgmtClient.VirtualAddress,
            comparison.ProxyClientToServer,
            comparison.ProxyServerToClient,
            comparison.ManagementBytesReceived,
            comparison.ManagementBytesSent,
            comparison.DeltaClientToServer,
            comparison.DeltaServerToClient,
            (DateTime.UtcNow - cacheFetchedAtUtc).TotalSeconds);
    }
}
