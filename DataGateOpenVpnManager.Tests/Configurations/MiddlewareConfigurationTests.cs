using DataGateOpenVpnManager.Configurations;
using DataGateOpenVpnManager.Services.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace DataGateOpenVpnManager.Tests.Configurations;

public class MiddlewareConfigurationTests
{
    [Fact]
    public void ConfigureMiddleware_RegistersMiddlewares_WithoutThrow()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddLogging();
        builder.Services.AddSingleton(Mock.Of<IMicroserviceJwtValidator>());

        var app = builder.Build();
        app.ConfigureMiddleware();

        Assert.NotNull(app);
    }
}
