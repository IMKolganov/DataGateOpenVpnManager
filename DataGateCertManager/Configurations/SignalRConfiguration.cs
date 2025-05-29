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

            try
            {
                client.ConnectAsync(CancellationToken.None).GetAwaiter().GetResult();
                logger.LogInformation("Connected to OpenVPN management at {Host}:{Port}", options.Host, options.Port);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to connect to OpenVPN management at {Host}:{Port}", options.Host, options.Port);
                // optionally rethrow or handle fallback
                throw;
            }

            return client;
        });

        services.AddSingleton<OpenVpnManagementSignalService>();
        services.AddSignalR();
    }
}