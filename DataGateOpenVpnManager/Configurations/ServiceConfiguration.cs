using System.Threading.RateLimiting;
using DataGateOpenVpnManager.Helpers;
using DataGateOpenVpnManager.Services;
using DataGateOpenVpnManager.Services.EasyRsaServices;
using DataGateOpenVpnManager.Services.EasyRsaServices.Interfaces;
using DataGateOpenVpnManager.Services.Interfaces;
using DataGateOpenVpnManager.Services.LocalIssuance;
using DataGateOpenVpnManager.Services.OpenVpnTelnet;
using DataGateOpenVpnManager.Services.Proxy;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DataGateOpenVpnManager.Configurations;

public static class ServiceConfiguration
{
    public static void ConfigureServices(this IServiceCollection services, IConfiguration config)
    {
        // Core services — download wait while issuing (default 10s; override via env).
        services.Configure<OvpnIssuanceOptions>(config.GetSection(OvpnIssuanceOptions.SectionName));
        services.PostConfigure<OvpnIssuanceOptions>(options =>
        {
            var raw = Environment.GetEnvironmentVariable(OvpnIssuanceOptions.WaitTimeoutSecondsEnvVar);
            if (string.IsNullOrWhiteSpace(raw))
                raw = config[OvpnIssuanceOptions.WaitTimeoutSecondsEnvVar];
            if (int.TryParse(raw, out var seconds) && seconds > 0)
                options.WaitTimeoutSeconds = seconds;
        });
        services.AddSingleton<IOvpnIssuanceTracker, OvpnIssuanceTracker>();
        services.AddScoped<IOvpnFileService, OvpnFileService>();
        services.ConfigureLocalIssuance(config);

        // EasyRsa services — PKI mutex must be singleton so all scopes share one gate per path.
        services.AddSingleton<IEasyRsaPkiMutex, EasyRsaPkiMutex>();
        services.AddScoped<IEasyRsaService, EasyRsaService>();
        services.AddScoped<IEasyRsaParseDbService, EasyRsaParseDbService>();
        services.AddScoped<IBashCommandRunner, BashCommandRunner>();

        // OpenVpn services
        services.AddScoped<IOpenVpnServerService, OpenVpnServerService>();
        services.AddSingleton<IOpenVpnProcessRunner, LinuxOpenVpnProcessRunner>();
        services.AddSingleton<IOpenVpnProcessService, OpenVpnProcessService>();

        // Rate Limiting
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.User?.Identity?.Name ?? context.Request.Headers.Host.ToString(),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 100,
                        Window = TimeSpan.FromMinutes(1)
                    }));
        });

        services.ConfigureEasyRsa(config);

        services.AddMemoryCache();
        services.AddHttpClient<IExternalIpAddressService, ExternalIpAddressService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        // HttpClient for MicroserviceJwtValidator
        services.AddHttpClient<MicroserviceJwtValidator>(client =>
        {
            client.BaseAddress = BackendBaseAddress.TryResolve(config);
        });

        services.AddSingleton<IMicroserviceJwtValidator>(sp =>
        {
            var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(MicroserviceJwtValidator));
            var logger = sp.GetRequiredService<ILogger<MicroserviceJwtValidator>>();
            return new MicroserviceJwtValidator(client, logger);
        });

        services.AddHostedService<MicroserviceJwtValidatorInitializer>();

        services.AddHttpClient(VpnServerAnnounceHostedService.HttpClientName, client =>
        {
            client.BaseAddress = BackendBaseAddress.TryResolve(config);
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHostedService<VpnServerAnnounceHostedService>();

        services.ConfigureProxy(config);
        services.ConfigurePiHole(config);

        services.AddControllers().AddNewtonsoftJson();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
    }

    /// <summary>
    /// Node key, registry, outbox and delivery worker exist only with LOCAL_ISSUANCE_ENABLED=true.
    /// </summary>
    public static void ConfigureLocalIssuance(this IServiceCollection services, IConfiguration config)
    {
        var options = LocalIssuanceOptions.FromConfiguration(config);
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        var lookup = ServiceClientLookupOptions.FromConfiguration(config);
        services.AddSingleton(lookup);
        if (lookup.Enabled)
        {
            services.AddSingleton<ILocalProfileRegistry, LocalProfileRegistry>();
            services.AddScoped<IServiceClientLookup, ServiceClientLookup>();
        }

        if (!options.Enabled)
            return;

        services.AddSingleton<INodeIdentity, NodeIdentity>();
        services.TryAddSingleton<ILocalProfileRegistry, LocalProfileRegistry>();
        services.AddSingleton<ILocalIssuanceOutbox, LocalIssuanceOutbox>();
        services.AddScoped<ILocalProfileService, LocalProfileService>();

        services.AddHttpClient(LocalIssuanceEventDeliveryService.HttpClientName, client =>
        {
            client.BaseAddress = BackendBaseAddress.TryResolve(config);
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHostedService<LocalIssuanceEventDeliveryService>();
    }
}
