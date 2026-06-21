using DataGateOpenVpnManager.Services.OpenVpnTelnet;

namespace DataGateOpenVpnManager.Services.Proxy;

public interface IOpenVpnManagementStatusCache
{
    OpenVpnManagementStatusSnapshot? GetSnapshot();
    Task RefreshAsync(CancellationToken cancellationToken);
}

public sealed record OpenVpnManagementStatusSnapshot(
    DateTime FetchedAtUtc,
    string RawStatus,
    IReadOnlyList<OpenVpnManagementClientEntry> Clients,
    bool IsValid);

public sealed class OpenVpnManagementStatusCache(
    OpenVpnManagementSignalService management,
    IProxySessionAuditService audit,
    ILogger<OpenVpnManagementStatusCache> logger) : IOpenVpnManagementStatusCache
{
    private readonly object _sync = new();
    private OpenVpnManagementStatusSnapshot? _snapshot;

    public OpenVpnManagementStatusSnapshot? GetSnapshot()
    {
        lock (_sync)
            return _snapshot;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var raw = await management.SendCommandAsync("status 3", cancellationToken);
            if (raw.StartsWith("Command timed out", StringComparison.Ordinal)
                || raw.StartsWith("Error while sending command", StringComparison.Ordinal))
            {
                logger.LogWarning("[ManagementStatusCache] refresh failed: {Error}", raw);
                audit.Record(new ProxySessionAuditEntry
                {
                    AtUtc = DateTime.UtcNow,
                    Event = "mgmt.cache.refresh_failed",
                    Decision = "skip",
                    Reason = raw
                });
                return;
            }

            var clients = OpenVpnManagementStatusParser.ParseClientList(raw);
            OpenVpnManagementStatusSnapshot snapshot;
            lock (_sync)
            {
                snapshot = new OpenVpnManagementStatusSnapshot(
                    DateTime.UtcNow,
                    raw,
                    clients,
                    IsValid: true);
                _snapshot = snapshot;
            }

            var details = ProxyAuditDetails.ForSnapshot(snapshot);
            details["mgmtClients"] = string.Join(',', clients.Select(c => $"{c.CommonName}@{c.RealAddress}"));
            audit.Record(new ProxySessionAuditEntry
            {
                AtUtc = DateTime.UtcNow,
                Event = "mgmt.cache.refreshed",
                Decision = "ok",
                Reason = $"clientCount={clients.Count}",
                Details = details
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[ManagementStatusCache] refresh failed");
            audit.Record(new ProxySessionAuditEntry
            {
                AtUtc = DateTime.UtcNow,
                Event = "mgmt.cache.refresh_failed",
                Decision = "error",
                Reason = ex.Message
            });
        }
    }
}
