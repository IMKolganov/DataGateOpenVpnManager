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
    /// <summary>sockaddr_un.sun_path is 108 bytes including the trailing NUL.</summary>
    public const int MaxUnixSocketPathBytes = 107;

    public bool Enabled { get; init; }

    /// <summary>Private (0700) directory: admin socket, node key, registry, outbox.</summary>
    public string Directory { get; init; } = string.Empty;

    public string SocketPath { get; init; } = string.Empty;

    public string NodeKeyPath => Path.Combine(Directory, "node-identity.key");
    public string RegistryPath => Path.Combine(Directory, "profiles.json");
    public string OutboxDir => Path.Combine(Directory, "outbox");
    public string RejectedDir => Path.Combine(Directory, "outbox-rejected");
    public string ClientTemplatePath => Path.Combine(Directory, "client-template.ovpn");

    public static LocalIssuanceOptions FromConfiguration(IConfiguration configuration)
    {
        var dataDir = NullIfWhiteSpace(configuration["DATA_DIR"]) ?? "/mnt";
        var dir = NullIfWhiteSpace(configuration[DirKey]) ?? Path.Combine(dataDir, "local-issuance");

        return new LocalIssuanceOptions
        {
            Enabled = OvpnNodeClientSettings.IsTruthy(configuration[EnabledKey]),
            Directory = dir,
            SocketPath = NullIfWhiteSpace(configuration[SocketKey]) ?? Path.Combine(dir, "admin.sock"),
        };
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
