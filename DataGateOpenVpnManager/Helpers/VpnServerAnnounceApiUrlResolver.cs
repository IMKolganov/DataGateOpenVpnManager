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
    private static readonly TimeSpan DomainDnsTimeout = TimeSpan.FromSeconds(2);

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
        var value = configuration[PublicApiUrlKey];
        return string.IsNullOrWhiteSpace(value) ? null : EnsureTrailingSlash(value.Trim());
    }

    /// <summary>
    /// Site <c>PUBLIC_IP</c> from install — prefer over external ifconfig-style lookups.
    /// Reads <see cref="IConfiguration"/> only (host builder already maps process env into config).
    /// </summary>
    public static string? GetConfiguredPublicIp(IConfiguration configuration)
    {
        if (TryParseConfiguredPublicIp(configuration[PublicIpKey], out var ip))
            return ip;

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

        // PUBLIC_API_URL is usually https://wss-domain/ — resolve its A-record when PUBLIC_IP is missing.
        var fromApiUrlHost = await TryResolvePublicApiUrlHostIpv4Async(configuration, cancellationToken);
        if (fromApiUrlHost is not null)
            return fromApiUrlHost;

        return await externalIpAddressService.GetPublicIpAddressAsync(cancellationToken);
    }

    internal static async Task<string?> TryResolvePublicApiUrlHostIpv4Async(
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var apiUrl = GetConfiguredPublicApiUrl(configuration);
        if (string.IsNullOrWhiteSpace(apiUrl)
            || !Uri.TryCreate(apiUrl, UriKind.Absolute, out var uri)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            return null;
        }

        if (TryParseConfiguredPublicIp(uri.Host, out var hostAsIp))
            return hostAsIp;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(DomainDnsTimeout);
            var entries = await Dns.GetHostAddressesAsync(uri.Host, timeout.Token);
            var ipv4 = entries.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (ipv4 is null)
                return null;

            return TryParseConfiguredPublicIp(ipv4.ToString(), out var ip) ? ip : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch
        {
            return null;
        }
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
        var raw = configuration["API_PORT"];
        if (int.TryParse(raw, out var port) && port > 0)
            return port;
        return DefaultApiPort;
    }

    public static string EnsureTrailingSlash(string url) =>
        url.EndsWith('/') ? url : url + "/";
}
