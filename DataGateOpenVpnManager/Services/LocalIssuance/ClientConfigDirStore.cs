using System.Net;
using DataGateOpenVpnManager.Helpers;
using DataGateOpenVpnManager.Services.Proxy;

namespace DataGateOpenVpnManager.Services.LocalIssuance;

public sealed class ClientConfigDirStore(
    LocalIssuanceOptions options,
    ILogger<ClientConfigDirStore> logger,
    IOpenVpnManagementStatusCache? managementStatus = null) : IClientConfigDirStore
{
    internal const string ManagedHeader = "# Managed by DataGateOpenVpnManager (local issuance). Do not edit by hand.";

    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsEnabled => !string.IsNullOrWhiteSpace(options.ClientConfigDir);
    public bool IsExclusive => IsEnabled && options.CcdExclusive;

    public async Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        return new Releaser(_gate);
    }

    public async Task ValidateStaticIpAsync(string commonName, string staticIp, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
            throw new LocalIssuanceValidationException(
                "A static IP needs client-config-dir: set CLIENT_CONFIG_DIR on the node and restart it.");

        IPAddress address;
        try
        {
            address = VpnSubnet.ParseIPv4(staticIp, "StaticIp");
        }
        catch (ArgumentException ex)
        {
            throw new LocalIssuanceValidationException(ex.Message);
        }

        var subnet = VpnSubnet.Parse(options.VpnSubnet, options.VpnNetmask);
        if (!subnet.IsAssignableClientAddress(address, out var reason))
            throw new LocalIssuanceValidationException(reason);

        // OpenVPN's pool does not skip ifconfig-push addresses; only the reserved range is out of its reach.
        if (options.StaticIpReserved > 0 && !subnet.IsInStaticReservedRange(address, options.StaticIpReserved))
        {
            var (first, last) = subnet.StaticReservedRange(options.StaticIpReserved);
            throw new LocalIssuanceValidationException(
                $"{address} is in OpenVPN's dynamic pool; static IPs must be within {first}-{last} " +
                $"({LocalIssuanceOptions.StaticIpReservedKey}={options.StaticIpReserved}).");
        }

        var normalized = address.ToString();
        foreach (var (cn, ip) in ReadStaticAssignments())
        {
            if (ip == normalized && !string.Equals(cn, commonName, StringComparison.Ordinal))
                throw new LocalIssuanceValidationException($"{normalized} is already assigned to '{cn}' (ccd).", isConflict: true);
        }

        foreach (var (cn, ip) in ReadPoolAssignments())
        {
            if (ip == normalized && !string.Equals(cn, commonName, StringComparison.Ordinal))
                throw new LocalIssuanceValidationException(
                    $"{normalized} is held by '{cn}' in the dynamic pool ({options.IppFile}).", isConflict: true);
        }

        foreach (var (cn, ip) in await ReadConnectedAddressesAsync(cancellationToken))
        {
            if (ip == normalized && !string.Equals(cn, commonName, StringComparison.Ordinal))
                throw new LocalIssuanceValidationException(
                    $"{normalized} is in use right now by connected client '{cn}'.", isConflict: true);
        }
    }

    public async Task WriteAsync(string commonName, string? staticIp, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
            return;

        var dir = options.ClientConfigDir!;
        Directory.CreateDirectory(dir);

        var lines = new List<string> { ManagedHeader };
        if (!string.IsNullOrWhiteSpace(staticIp))
        {
            var subnet = VpnSubnet.Parse(options.VpnSubnet, options.VpnNetmask);
            lines.Add($"ifconfig-push {VpnSubnet.ParseIPv4(staticIp, "StaticIp")} {subnet.Netmask}");
        }

        var path = PathFor(commonName);
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, string.Join('\n', lines) + "\n", cancellationToken);
        // OpenVPN reads ccd after dropping privileges to nobody.
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temp,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        File.Move(temp, path, overwrite: true);
        logger.LogInformation("Wrote ccd file for {CommonName} (static IP {StaticIp}).", commonName, staticIp ?? "-");
    }

    public void Remove(string commonName)
    {
        if (!IsEnabled)
            return;

        var path = PathFor(commonName);
        if (!File.Exists(path))
            return;

        File.Delete(path);
        logger.LogInformation("Removed ccd file for {CommonName}.", commonName);
    }

    public IReadOnlyDictionary<string, string> ReadStaticAssignments()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!IsEnabled || !Directory.Exists(options.ClientConfigDir))
            return result;

        foreach (var file in Directory.EnumerateFiles(options.ClientConfigDir!))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith('.') || name.EndsWith(".tmp", StringComparison.Ordinal))
                continue;

            foreach (var line in File.ReadLines(file))
            {
                var parts = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2
                    && parts[0].Equals("ifconfig-push", StringComparison.OrdinalIgnoreCase)
                    && IPAddress.TryParse(parts[1], out var ip))
                {
                    result[name] = ip.ToString();
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>ipp.txt lines: <c>cn,ipv4[,ipv6]</c>.</summary>
    private IEnumerable<(string Cn, string Ip)> ReadPoolAssignments()
    {
        if (!File.Exists(options.IppFile))
            yield break;

        foreach (var line in File.ReadLines(options.IppFile))
        {
            var parts = line.Split(',');
            if (parts.Length >= 2 && IPAddress.TryParse(parts[1].Trim(), out var ip))
                yield return (parts[0].Trim(), ip.ToString());
        }
    }

    /// <summary>
    /// Live virtual addresses from the management interface: OpenVPN rewrites ipp.txt only every few minutes,
    /// so a dynamic client connected since then is invisible there. Skipped when management cannot answer now.
    /// </summary>
    private async Task<IEnumerable<(string Cn, string Ip)>> ReadConnectedAddressesAsync(CancellationToken cancellationToken)
    {
        if (managementStatus is null)
            return [];

        var requestedAt = DateTime.UtcNow;
        await managementStatus.RefreshAsync(cancellationToken);
        var snapshot = managementStatus.GetSnapshot();
        if (snapshot is not { IsValid: true } || snapshot.FetchedAtUtc < requestedAt)
        {
            logger.LogWarning("OpenVPN management status unavailable; static IP checked against ccd and ipp.txt only.");
            return [];
        }

        return snapshot.Clients
            .Where(c => IPAddress.TryParse(c.VirtualAddress, out _))
            .Select(c => (c.CommonName, IPAddress.Parse(c.VirtualAddress).ToString()));
    }

    // Dashboard-issued CNs are not held to CommonNameValidator; only refuse names that escape the ccd dir.
    private string PathFor(string commonName)
    {
        if (string.IsNullOrWhiteSpace(commonName)
            || commonName is "." or ".."
            || commonName.IndexOfAny(['/', '\\', '\0']) >= 0)
            throw new LocalIssuanceValidationException($"CommonName '{commonName}' cannot be used as a ccd file name.");
        return Path.Combine(options.ClientConfigDir!, commonName);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}
