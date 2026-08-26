using System.Net;
using System.Net.Sockets;
using DataGateOpenVpnManager.Services.Interfaces;
using Microsoft.Extensions.Configuration;

namespace DataGateOpenVpnManager.Helpers;

/// <summary>
/// Resolves the public manager ApiUrl used when announcing this node to the dashboard.
/// </summary>
public static class VpnServerAnnounceApiUrlResolver
{
    public const string PublicApiUrlKey = "PUBLIC_API_URL";
    public const string PublicIpKey = "PUBLIC_IP";
    public const int DefaultApiPort = 5010;

    /// <summary>
    /// Prefer an explicit public URL; otherwise build <c>http://{publicIp}:{apiPort}/</c>.
    /// </summary>
    public static string? Resolve(string? publicApiUrl, string? publicIp, int apiPort)
    {
        if (!string.IsNullOrWhiteSpace(publicApiUrl))
            return EnsureTrailingSlash(publicApiUrl.Trim());

        if (string.IsNullOrWhiteSpace(publicIp) || apiPort <= 0)
            return null;

        return $"http://{publicIp.Trim()}:{apiPort}/";
    }

    public static string? GetConfiguredPublicApiUrl(IConfiguration configuration)
    {
        var value = Environment.GetEnvironmentVariable(PublicApiUrlKey)
            ?? configuration[PublicApiUrlKey];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Site <c>PUBLIC_IP</c> from install — prefer over external ifconfig-style lookups.
    /// </summary>
    public static string? GetConfiguredPublicIp(IConfiguration configuration)
    {
        foreach (var raw in new[]
                 {
                     Environment.GetEnvironmentVariable(PublicIpKey),
                     configuration[PublicIpKey]
                 })
        {
            if (TryParseConfiguredPublicIp(raw, out var ip))
                return ip;
        }

        return null;
    }

    public static async Task<string?> ResolvePublicIpForAnnounceAsync(
        IConfiguration configuration,
        IExternalIpAddressService externalIpAddressService,
        CancellationToken cancellationToken)
    {
        var configured = GetConfiguredPublicIp(configuration);
        if (configured is not null)
            return configured;

        return await externalIpAddressService.GetPublicIpAddressAsync(cancellationToken);
    }

    internal static bool TryParseConfiguredPublicIp(string? raw, out string ip)
    {
        ip = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var candidate = raw.Split(['\r', '\n', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries)[0];
        if (!IPAddress.TryParse(candidate, out var address))
            return false;

        if (address.AddressFamily != AddressFamily.InterNetwork)
            return false;

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any))
            return false;

        ip = address.ToString();
        return true;
    }

    public static int ResolveApiPort(IConfiguration configuration)
    {
        var raw = Environment.GetEnvironmentVariable("API_PORT")
            ?? configuration["API_PORT"];
        if (int.TryParse(raw, out var port) && port > 0)
            return port;
        return DefaultApiPort;
    }

    public static string EnsureTrailingSlash(string url) =>
        url.EndsWith('/') ? url : url + "/";
}
