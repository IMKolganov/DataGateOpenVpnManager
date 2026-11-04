using DataGateOpenVpnManager.Helpers;

namespace DataGateOpenVpnManager.Services.LocalIssuance;

public sealed class ServiceClientLookupOptions
{
    public const string EnabledKey = "SERVICE_CLIENT_LOOKUP";
    public const string PortKey = "SERVICE_CLIENT_LOOKUP_PORT";
    public const int DefaultPort = 5012;
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);

    public bool Enabled { get; init; }
    public int Port { get; init; } = DefaultPort;

    public static ServiceClientLookupOptions FromConfiguration(IConfiguration configuration) => new()
    {
        Enabled = OvpnNodeClientSettings.IsTruthy(configuration[EnabledKey]),
        Port = configuration.GetValue(PortKey, DefaultPort),
    };
}
