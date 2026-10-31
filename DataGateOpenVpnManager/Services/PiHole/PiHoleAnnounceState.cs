using DataGateOpenVpnManager.Models;

namespace DataGateOpenVpnManager.Services.PiHole;

/// <summary>What the startup announce does with this node's Pi-hole credentials.</summary>
internal static class PiHoleAnnounceState
{
    public const string Included = "Included";
    public const string NoCredentials = "NoCredentials";
    public const string OmittedInsecureBackend = "OmittedInsecureBackend";
    public const string BackendNotConfigured = "BackendNotConfigured";

    public static string Resolve(PiHoleOptions options, Uri? backend)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl) || string.IsNullOrWhiteSpace(options.AppPassword))
            return NoCredentials;

        if (backend is null)
            return BackendNotConfigured;

        return backend.Scheme != Uri.UriSchemeHttps && !backend.IsLoopback
            ? OmittedInsecureBackend
            : Included;
    }
}
