using DataGateCertManager.Models;
using DataGateCertManager.Services.OpenVpnTelnet;
using Microsoft.Extensions.Options;

namespace DataGateCertManager.Configurations;

public static class SignalRConfiguration
{
    public static void ConfigureSignalR(this IServiceCollection services, IConfiguration config)
    {
        // Load OpenVPN management config from appsettings or environment
        services.Configure<OpenVpnManagementOptions>(config.GetSection("OpenVpnManagement"));

        // Register TelnetClient as singleton with config and logger
        services.AddSingleton<TelnetClient>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<TelnetClient>>();
            var options = sp.GetRequiredService<IOptions<OpenVpnManagementOptions>>().Value;

            var client = new TelnetClient(options.Host, options.Port, logger);

            var retryDelay = TimeSpan.FromSeconds(1);
            var maxAttempts = 0;
            var attempt = 0;

            while (true)
            {
                try
                {
                    client.ConnectAsync(CancellationToken.None).GetAwaiter().GetResult();
                    logger.LogInformation("✅ Connected to OpenVPN management at {Host}:{Port}", options.Host, options.Port);
                    break;
                }
                catch (Exception ex)
                {
                    attempt++;
                    logger.LogWarning(ex, "⏳ Attempt {Attempt}: Failed to connect to OpenVPN management at {Host}:{Port}", attempt, options.Host, options.Port);

                    if (maxAttempts > 0 && attempt >= maxAttempts)
                    {
                        logger.LogError("❌ Max connection attempts reached. Giving up.");
                        throw;
                    }

                    Thread.Sleep(retryDelay);
                }
            }

            return client;
        });

        services.AddSingleton<OpenVpnManagementSignalService>();
        services.AddSignalR();
    }
}