using System.Globalization;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy;

namespace DataGateOpenVpnManager.Services.Proxy;

internal static class ProxyAuditDetails
{
    public static Dictionary<string, string> ForConnection(ActiveProxyConnection connection) =>
        new()
        {
            ["proto"] = connection.Protocol.ToString(),
            ["local"] = $"{connection.LocalProxyIp}:{connection.LocalProxyPort.ToString(CultureInfo.InvariantCulture)}",
            ["client"] = $"{connection.RealClientIp}:{connection.RealClientPort.ToString(CultureInfo.InvariantCulture)}",
            ["connectedAtUtc"] = connection.ConnectedAtUtc.ToString("O", CultureInfo.InvariantCulture)
        };

    public static Dictionary<string, string> ForSnapshot(OpenVpnManagementStatusSnapshot? snapshot)
    {
        if (snapshot is null)
            return new Dictionary<string, string> { ["cache"] = "null" };

        return new Dictionary<string, string>
        {
            ["cacheFetchedAtUtc"] = snapshot.FetchedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            ["cacheAgeSec"] = Math.Round((DateTime.UtcNow - snapshot.FetchedAtUtc).TotalSeconds, 1)
                .ToString(CultureInfo.InvariantCulture),
            ["mgmtClientCount"] = snapshot.Clients.Count.ToString(CultureInfo.InvariantCulture)
        };
    }
}
