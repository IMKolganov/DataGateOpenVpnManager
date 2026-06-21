using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy;
using DataGateOpenVpnManager.Models;

namespace DataGateOpenVpnManager.Services.Proxy;

internal static class ProxyManagementPeerDiagnostics
{
    public static bool CanEvaluatePeerPresence(
        OpenVpnManagementStatusSnapshot? snapshot,
        OpenVpnProxyOptions options,
        out string? skipReason)
    {
        if (snapshot is null || !snapshot.IsValid)
        {
            skipReason = "management_cache_unavailable";
            return false;
        }

        var cacheAge = DateTime.UtcNow - snapshot.FetchedAtUtc;
        var maxCacheAge = TimeSpan.FromSeconds(Math.Max(10, options.ManagementStatusRefreshSeconds * 2));
        if (cacheAge > maxCacheAge)
        {
            skipReason = "management_cache_stale";
            return false;
        }

        if (snapshot.Clients.Count == 0)
        {
            skipReason = "client_list_empty";
            return false;
        }

        skipReason = null;
        return true;
    }

    public static bool IsLikelyZombie(
        ActiveProxyConnection connection,
        OpenVpnManagementClientEntry? mgmtClient,
        OpenVpnManagementStatusSnapshot snapshot)
    {
        if (mgmtClient is not null)
            return false;

        if (snapshot.FetchedAtUtc < connection.ConnectedAtUtc)
            return false;

        return true;
    }
}
