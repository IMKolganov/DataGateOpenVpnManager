namespace DataGateOpenVpnManager.Services.PiHole;

public static class PiHoleSubnetFilter
{
    public static bool Matches(string clientIp, string? subnetPrefix)
    {
        if (string.IsNullOrWhiteSpace(subnetPrefix))
            return true;

        return clientIp.StartsWith(subnetPrefix.Trim(), StringComparison.Ordinal);
    }

    public static IReadOnlyList<PiHoleQueryRecord> Apply(
        IEnumerable<PiHoleQueryRecord> records,
        string? subnetPrefix)
    {
        if (string.IsNullOrWhiteSpace(subnetPrefix))
            return records.ToList();

        return records
            .Where(r => Matches(r.ClientIp, subnetPrefix))
            .ToList();
    }
}
