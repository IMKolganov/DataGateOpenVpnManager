using System.Threading.RateLimiting;
using DataGateCertManager.Helpers;
using DataGateCertManager.Services;
using DataGateCertManager.Services.EasyRsaServices;
using DataGateCertManager.Services.EasyRsaServices.Interfaces;
using DataGateCertManager.Services.Interfaces;
using Microsoft.Extensions.Configuration;

namespace DataGateCertManager.Configurations;

public static class ServiceConfiguration
{
    public static void ConfigureServices(this IServiceCollection services, IConfiguration config)
    {
        // Core services
        services.AddScoped<IOvpnFileService, OvpnFileService>();

        // EasyRsa services
        services.AddScoped<IEasyRsaService, EasyRsaService>();
        services.AddScoped<IEasyRsaParseDbService, EasyRsaParseDbService>();
        services.AddScoped<IBashCommandRunner, BashCommandRunner>();

        // OpenVpn services
        services.AddScoped<IOpenVpnServerService, OpenVpnServerService>();

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

        services.AddSingleton<IEasyRsaPathResolver, EasyRsaPathResolver>();

        // HttpClient for MicroserviceJwtValidator
        services.AddHttpClient<IMicroserviceJwtValidator, MicroserviceJwtValidator>(client =>
        {
            var baseUrl = config["Backend:BaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException("Missing configuration: Backend:BaseUrl");

            client.BaseAddress = new Uri(baseUrl);
        });

        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
    }
}
