using DataGateOpenVpnManager.Models;

namespace DataGateOpenVpnManager.Services.PiHole;

internal sealed class PiHoleEnvOverrides
{
    public bool? Enabled { get; init; }

    public string? BaseUrl { get; init; }

    public string? AppPassword { get; init; }

    public int? PollIntervalSeconds { get; init; }

    public int? BatchSize { get; init; }

    public int? LookbackSeconds { get; init; }

    public string? ClientSubnetPrefix { get; init; }

    public bool HasAny =>
        Enabled.HasValue ||
        !string.IsNullOrWhiteSpace(BaseUrl) ||
        !string.IsNullOrWhiteSpace(AppPassword) ||
        PollIntervalSeconds.HasValue ||
        BatchSize.HasValue ||
        LookbackSeconds.HasValue ||
        !string.IsNullOrWhiteSpace(ClientSubnetPrefix);

    public static PiHoleEnvOverrides FromConfiguration(IConfiguration config)
    {
        bool? enabled = null;
        if (bool.TryParse(config["PIHOLE_ENABLED"], out var legacyEnabled))
            enabled = legacyEnabled;
        else if (bool.TryParse(Environment.GetEnvironmentVariable("PiHole__Enabled"), out var standardEnabled))
            enabled = standardEnabled;

        var baseUrl = FirstNonEmpty(config["PIHOLE_BASE_URL"], Environment.GetEnvironmentVariable("PiHole__BaseUrl"));
        var password = FirstNonEmpty(config["PIHOLE_APP_PASSWORD"], Environment.GetEnvironmentVariable("PiHole__AppPassword"));

        int? pollInterval = null;
        if (int.TryParse(config["PIHOLE_POLL_INTERVAL_SEC"], out var legacyPoll))
            pollInterval = legacyPoll;
        else if (int.TryParse(Environment.GetEnvironmentVariable("PiHole__PollIntervalSeconds"), out var standardPoll))
            pollInterval = standardPoll;

        int? batchSize = null;
        if (int.TryParse(config["PIHOLE_BATCH_SIZE"], out var legacyBatch))
            batchSize = legacyBatch;
        else if (int.TryParse(Environment.GetEnvironmentVariable("PiHole__BatchSize"), out var standardBatch))
            batchSize = standardBatch;

        int? lookback = null;
        if (int.TryParse(config["PIHOLE_LOOKBACK_SEC"], out var legacyLookback))
            lookback = legacyLookback;
        else if (int.TryParse(Environment.GetEnvironmentVariable("PiHole__LookbackSeconds"), out var standardLookback))
            lookback = standardLookback;

        var subnet = FirstNonEmpty(
            config["PIHOLE_CLIENT_SUBNET_PREFIX"],
            Environment.GetEnvironmentVariable("PiHole__ClientSubnetPrefix"));

        return new PiHoleEnvOverrides
        {
            Enabled = enabled,
            BaseUrl = baseUrl,
            AppPassword = password,
            PollIntervalSeconds = pollInterval,
            BatchSize = batchSize,
            LookbackSeconds = lookback,
            ClientSubnetPrefix = subnet
        };
    }

    public void ApplyTo(PiHoleOptions options)
    {
        if (Enabled.HasValue)
            options.Enabled = Enabled.Value;

        if (!string.IsNullOrWhiteSpace(BaseUrl))
            options.BaseUrl = BaseUrl;

        if (!string.IsNullOrWhiteSpace(AppPassword))
            options.AppPassword = AppPassword;

        if (PollIntervalSeconds.HasValue)
            options.PollIntervalSeconds = PollIntervalSeconds.Value;

        if (BatchSize.HasValue)
            options.BatchSize = BatchSize.Value;

        if (LookbackSeconds.HasValue)
            options.LookbackSeconds = LookbackSeconds.Value;

        if (!string.IsNullOrWhiteSpace(ClientSubnetPrefix))
            options.ClientSubnetPrefix = ClientSubnetPrefix;
    }

    public static void ApplyLegacyEnv(IConfiguration config, PiHoleOptions options) =>
        FromConfiguration(config).ApplyTo(options);

    private static string? FirstNonEmpty(string? primary, string? secondary)
    {
        if (!string.IsNullOrWhiteSpace(primary))
            return primary.Trim();

        return string.IsNullOrWhiteSpace(secondary) ? null : secondary.Trim();
    }
}
