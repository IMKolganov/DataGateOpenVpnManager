using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using DataGateMonitor.SharedModels.DataGateMonitor.VpnServers.Requests;
using DataGateMonitor.SharedModels.Enums;
using DataGateOpenVpnManager.Helpers;
using DataGateOpenVpnManager.Services.Interfaces;
using DataGateOpenVpnManager.Services.LocalIssuance;
using Newtonsoft.Json;

namespace DataGateOpenVpnManager.Services;

/// <summary>
/// On startup, announces this OpenVPN manager to the dashboard discovery endpoint.
/// Failures are logged only — never crash the host.
/// </summary>
public sealed class VpnServerAnnounceHostedService(
    IHttpClientFactory httpClientFactory,
    IExternalIpAddressService externalIpAddressService,
    IConfiguration configuration,
    LocalIssuanceOptions localIssuanceOptions,
    IServiceProvider services,
    ILogger<VpnServerAnnounceHostedService> logger) : BackgroundService
{
    /// <summary>Extra announce field (not in SharedModels yet): node event-signing key, bound to the server on approve.</summary>
    public const string NodePublicKeyField = "NodePublicKeyPem";

    public const string HttpClientName = nameof(VpnServerAnnounceHostedService);
    public const string DiscoverPath = "api/open-vpn-servers/discover";
    private const int MaxAttempts = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await AnnounceWithRetriesAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "VPN server announce failed unexpectedly; continuing without discovery registration.");
        }
    }

    private async Task AnnounceWithRetriesAsync(CancellationToken cancellationToken)
    {
        if (BackendBaseAddress.TryResolve(configuration) is null)
        {
            logger.LogError(
                "Backend:BaseUrl is missing or not an absolute http(s) URL. Discovery registration stays pending until Backend__BaseUrl is set and the container is restarted.");
            return;
        }

        string? publicIp = null;
        try
        {
            publicIp = await VpnServerAnnounceApiUrlResolver.ResolvePublicIpForAnnounceAsync(
                configuration, externalIpAddressService, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to resolve public IP for VPN server announce.");
        }

        var apiPort = VpnServerAnnounceApiUrlResolver.ResolveApiPort(configuration);
        var apiUrl = VpnServerAnnounceApiUrlResolver.Resolve(
            VpnServerAnnounceApiUrlResolver.GetConfiguredPublicApiUrl(configuration),
            publicIp,
            apiPort);

        if (string.IsNullOrWhiteSpace(apiUrl))
        {
            logger.LogWarning(
                "Skipping VPN server announce: ApiUrl could not be resolved (set {PublicApiUrlKey} or ensure public IP is available).",
                VpnServerAnnounceApiUrlResolver.PublicApiUrlKey);
            return;
        }

        var request = new AnnounceVpnServerRequest
        {
            ServerType = VpnServerType.OpenVpn,
            ApiUrl = apiUrl,
            SuggestedName = ResolveSuggestedName(),
            PublicIp = publicIp,
            Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
            IsEnableWss = configuration.GetValue("IsEnableWss", false)
        };

        var client = httpClientFactory.CreateClient(HttpClientName);
        var json = BuildAnnounceJson(request);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                using var response = await client.PostAsync(DiscoverPath, content, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation(
                        "Announced OpenVPN manager to dashboard ({ApiUrl}, attempt {Attempt}/{MaxAttempts}, status {StatusCode}).",
                        apiUrl, attempt, MaxAttempts, (int)response.StatusCode);
                    return;
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning(
                    "VPN server announce attempt {Attempt}/{MaxAttempts} returned {StatusCode}: {Body}",
                    attempt, MaxAttempts, (int)response.StatusCode, Truncate(body, 256));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "VPN server announce attempt {Attempt}/{MaxAttempts} failed.",
                    attempt, MaxAttempts);
            }

            if (attempt < MaxAttempts)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                await Task.Delay(delay, cancellationToken);
            }
        }

        logger.LogWarning(
            "VPN server announce gave up after {MaxAttempts} attempts ({ApiUrl}).",
            MaxAttempts, apiUrl);
    }

    internal string BuildAnnounceJson(AnnounceVpnServerRequest request)
    {
        request.NodePublicKeyPem = localIssuanceOptions.Enabled
            ? services.GetRequiredService<INodeIdentity>().PublicKeyPem
            : null;
        return JsonConvert.SerializeObject(request);
    }

    private static string ResolveSuggestedName()
    {
        try
        {
            var host = System.Net.Dns.GetHostName();
            if (!string.IsNullOrWhiteSpace(host))
                return host.Trim();
        }
        catch
        {
            // fall through to MachineName
        }

        return Environment.MachineName;
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
            return value ?? string.Empty;
        return value[..max] + "…";
    }
}
