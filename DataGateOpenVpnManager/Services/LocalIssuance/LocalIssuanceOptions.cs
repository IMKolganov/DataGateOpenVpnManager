using DataGateOpenVpnManager.Helpers;

namespace DataGateOpenVpnManager.Services.LocalIssuance;

/// <summary>
/// Settings for node-side ("reverse") profile issuance. Everything is off unless
/// <c>LOCAL_ISSUANCE_ENABLED=true</c>, so existing nodes keep their behaviour.
/// </summary>
public sealed class LocalIssuanceOptions
{
    public const string EnabledKey = "LOCAL_ISSUANCE_ENABLED";
    public const string DirKey = "LOCAL_ISSUANCE_DIR";
    public const string SocketKey = "LOCAL_ADMIN_SOCKET";
    public const string ClientConfigDirKey = "CLIENT_CONFIG_DIR";
    public const string CcdExclusiveKey = "CCD_EXCLUSIVE";
    public const string IppFileKey = "IPP_FILE";
    public const string StaticIpReservedKey = "STATIC_IP_RESERVED";
    public const int DefaultStaticIpReserved = 32;

    /// <summary>sockaddr_un.sun_path is 108 bytes including the trailing NUL.</summary>
    public const int MaxUnixSocketPathBytes = 107;

    public bool Enabled { get; init; }

    /// <summary>Private (0700) directory: admin socket, node key, registry, outbox.</summary>
    public string Directory { get; init; } = string.Empty;

    public string SocketPath { get; init; } = string.Empty;

    /// <summary>Resolved client-config-dir, or null when ccd is disabled.</summary>
    public string? ClientConfigDir { get; init; }

    public bool CcdExclusive { get; init; }

    public string VpnSubnet { get; init; } = "10.51.28.0";
    public string VpnNetmask { get; init; } = "255.255.255.0";

    /// <summary>
    /// Addresses at the top of the subnet that server.conf keeps out of OpenVPN's dynamic pool for ifconfig-push;
    /// 0 = no reservation. Effective value, same rules as <c>static_ip_reserved_count</c> in scripts/server-conf.sh.
    /// </summary>
    public int StaticIpReserved { get; init; }

    /// <summary>ifconfig-pool-persist file written by OpenVPN (see server.conf).</summary>
    public string IppFile { get; init; } = "/etc/openvpn/ipp.txt";

    public string NodeKeyPath => Path.Combine(Directory, "node-identity.key");
    public string RegistryPath => Path.Combine(Directory, "profiles.json");
    public string OutboxDir => Path.Combine(Directory, "outbox");
    public string RejectedDir => Path.Combine(Directory, "outbox-rejected");
    public string ClientTemplatePath => Path.Combine(Directory, "client-template.ovpn");

    public static LocalIssuanceOptions FromConfiguration(IConfiguration configuration)
    {
        var dataDir = NullIfWhiteSpace(configuration["DATA_DIR"]) ?? "/mnt";
        var dir = NullIfWhiteSpace(configuration[DirKey]) ?? Path.Combine(dataDir, "local-issuance");

        var clientConfigDir = ResolveClientConfigDir(configuration[ClientConfigDirKey], dataDir);
        var vpnSubnet = NullIfWhiteSpace(configuration["VPN_SUBNET"]) ?? "10.51.28.0";
        var vpnNetmask = NullIfWhiteSpace(configuration["VPN_NETMASK"]) ?? "255.255.255.0";

        return new LocalIssuanceOptions
        {
            Enabled = OvpnNodeClientSettings.IsTruthy(configuration[EnabledKey]),
            Directory = dir,
            SocketPath = NullIfWhiteSpace(configuration[SocketKey]) ?? Path.Combine(dir, "admin.sock"),
            ClientConfigDir = clientConfigDir,
            CcdExclusive = OvpnNodeClientSettings.IsTruthy(configuration[CcdExclusiveKey]),
            VpnSubnet = vpnSubnet,
            VpnNetmask = vpnNetmask,
            StaticIpReserved = ResolveStaticIpReserved(
                configuration[StaticIpReservedKey], clientConfigDir is not null, vpnSubnet, vpnNetmask),
            IppFile = NullIfWhiteSpace(configuration[IppFileKey]) ?? "/etc/openvpn/ipp.txt",
        };
    }

    /// <summary>Same rules as <c>resolve_client_config_dir</c> in scripts/server-conf.sh.</summary>
    internal static string? ResolveClientConfigDir(string? raw, string dataDir)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (OvpnNodeClientSettings.IsTruthy(raw.Trim()))
            return Path.Combine(dataDir, "ccd");
        return raw.Trim().TrimEnd('/');
    }

    /// <summary>Same rules as <c>static_ip_reserved_count</c> in scripts/server-conf.sh.</summary>
    internal static int ResolveStaticIpReserved(string? raw, bool ccdEnabled, string vpnSubnet, string vpnNetmask)
    {
        if (!ccdEnabled)
            return 0;
        var wanted = DefaultStaticIpReserved;
        if (!string.IsNullOrWhiteSpace(raw) && (!int.TryParse(raw.Trim(), out wanted) || wanted < 0))
            return 0;
        try
        {
            return wanted < Helpers.VpnSubnet.Parse(vpnSubnet, vpnNetmask).DynamicPoolSize ? wanted : 0;
        }
        catch (ArgumentException)
        {
            return 0;
        }
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
