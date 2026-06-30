namespace DataGateOpenVpnManager.Services.OpenVpnTls;

/// <summary>
/// Who likely caused an OpenVPN TLS / tls-crypt error (for log enrichment and Wazuh filtering).
/// </summary>
public enum OpenVpnTlsErrorOrigin
{
    /// <summary>Internet scan or direct probe without our WSS proxy path.</summary>
    ExternalProbe,

    /// <summary>WSS proxy path (loopback) with a matched in-memory proxy session.</summary>
    AppViaProxy,

    /// <summary>Loopback peer but no active proxy session (race or local non-app client).</summary>
    LocalUnknown,

    /// <summary>Non-loopback peer that is not classified as a known external scan pattern.</summary>
    DirectClient
}
