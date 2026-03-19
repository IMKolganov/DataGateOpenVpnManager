using DataGateOpenVpnManager.Middlewares;
using DataGateOpenVpnManager.Services.Proxy;

namespace DataGateOpenVpnManager.Configurations;

public static class ProxyConfiguration
{
    public static void ConfigureMiddleware(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<IProxyConnectionHistoryService, ProxyConnectionHistoryService>();
        services.AddSingleton<IActiveProxyConnectionService, ActiveProxyConnectionService>();
    }
}