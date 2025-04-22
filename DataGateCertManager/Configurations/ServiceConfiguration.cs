using System.Threading.RateLimiting;
using DataGateCertManager.Services;
using DataGateCertManager.Services.EasyRsaServices;
using DataGateCertManager.Services.EasyRsaServices.Interfaces;
using DataGateCertManager.Services.Interfaces;

namespace DataGateCertManager.Configurations;

public static class ServiceConfiguration
{
    public static void ConfigureServices(this IServiceCollection services)
    {
        // Core services
        services.AddScoped<ICertVpnService, CertVpnService>();
        
        // EasyRsa services
        services.AddScoped<IEasyRsaService, EasyRsaService>();
        services.AddScoped<IEasyRsaParseDbService, EasyRsaParseDbService>();
        services.AddScoped<IEasyRsaExecCommandService, EasyRsaExecCommandService>();
        services.AddScoped<IBashCommandRunner, BashCommandRunner>();
        
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.User?.Identity?.Name ?? context.Request.Headers.Host.ToString(),
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 100,
                        Window = TimeSpan.FromMinutes(1)
                    }));
        });

        
        services.AddControllers();

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
    }
}
