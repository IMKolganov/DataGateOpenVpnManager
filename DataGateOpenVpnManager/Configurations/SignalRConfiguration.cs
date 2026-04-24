using DataGateOpenVpnManager.Hubs;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Models;
using DataGateOpenVpnManager.Services.OpenVpnTelnet;
using Microsoft.Extensions.Options;

namespace DataGateOpenVpnManager.Configurations;

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
            return new TelnetClient(options.Host, options.Port, logger);
        });

        services.AddSingleton<OpenVpnManagementSignalService>();
        services.AddSingleton<HubConnectionTracker>(); 
        services.AddSignalR();
    }
}