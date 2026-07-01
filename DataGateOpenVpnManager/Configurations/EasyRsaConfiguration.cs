using DataGateOpenVpnManager.Helpers;
using DataGateOpenVpnManager.Models;

namespace DataGateOpenVpnManager.Configurations;

public static class EasyRsaConfiguration
{
    public static void ConfigureEasyRsa(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<EasyRsaOptions>(config.GetSection("EasyRsa"));
        services.PostConfigure<EasyRsaOptions>(options => ApplyLegacyEnv(config, options));
        services.AddSingleton<IEasyRsaPathResolver, EasyRsaPathResolver>();
    }

    internal static void ApplyLegacyEnv(IConfiguration config, EasyRsaOptions options)
    {
        var mainPath = Environment.GetEnvironmentVariable("EASY_RSA_PATH");
        if (string.IsNullOrWhiteSpace(mainPath))
            mainPath = config["EASY_RSA_PATH"];
        if (!string.IsNullOrWhiteSpace(mainPath))
            options.MainPath = mainPath;

        var indexFileName = Environment.GetEnvironmentVariable("EASY_RSA_INDEX_FILE");
        if (string.IsNullOrWhiteSpace(indexFileName))
            indexFileName = config["EASY_RSA_INDEX_FILE"];
        if (!string.IsNullOrWhiteSpace(indexFileName))
            options.IndexFileName = indexFileName;

        var taKeyFileName = Environment.GetEnvironmentVariable("EASY_RSA_TA_KEY_FILE");
        if (string.IsNullOrWhiteSpace(taKeyFileName))
            taKeyFileName = config["EASY_RSA_TA_KEY_FILE"];
        if (!string.IsNullOrWhiteSpace(taKeyFileName))
            options.TaKeyFileName = taKeyFileName;
    }
}
