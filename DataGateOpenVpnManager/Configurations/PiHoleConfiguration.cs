using DataGateOpenVpnManager.Models;
using DataGateOpenVpnManager.Services.PiHole;

namespace DataGateOpenVpnManager.Configurations;

public static class PiHoleConfiguration
{
    public static void ConfigurePiHole(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<PiHoleOptions>(config.GetSection("PiHole"));
        services.PostConfigure<PiHoleOptions>(options => ApplyLegacyEnv(config, options));

        services.AddSingleton<IPiHoleRuntimeOptionsStore, PiHoleRuntimeOptionsStore>();
        services.AddSingleton<IPiHoleCollectorStatusStore, PiHoleCollectorStatusStore>();

        services.AddHttpClient<IPiHoleApiClient, PiHoleApiClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddSingleton<IPiHoleQueryCursorStore, PiHoleQueryCursorStore>();
        services.AddSingleton<IPiHoleClientIdentityResolver, PiHoleClientIdentityResolver>();
        services.AddHostedService<PiHoleQueryCollectorHostedService>();
    }

    private static void ApplyLegacyEnv(IConfiguration config, PiHoleOptions options)
    {
        if (bool.TryParse(config["PIHOLE_ENABLED"], out var enabled))
            options.Enabled = enabled;

        var baseUrl = config["PIHOLE_BASE_URL"];
        if (!string.IsNullOrWhiteSpace(baseUrl))
            options.BaseUrl = baseUrl;

        var password = config["PIHOLE_APP_PASSWORD"];
        if (!string.IsNullOrWhiteSpace(password))
            options.AppPassword = password;

        if (int.TryParse(config["PIHOLE_POLL_INTERVAL_SEC"], out var pollInterval))
            options.PollIntervalSeconds = pollInterval;

        if (int.TryParse(config["PIHOLE_BATCH_SIZE"], out var batchSize))
            options.BatchSize = batchSize;

        if (!string.IsNullOrWhiteSpace(config["PIHOLE_CLIENT_SUBNET_PREFIX"]))
            options.ClientSubnetPrefix = config["PIHOLE_CLIENT_SUBNET_PREFIX"]!;
    }
}
