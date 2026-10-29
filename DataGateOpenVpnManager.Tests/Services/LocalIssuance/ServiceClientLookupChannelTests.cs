using DataGateMonitor.SharedModels.DataGateOpenVpnManager.LocalIssuance.Responses;
using System.Net;
using DataGateOpenVpnManager.Controllers;
using DataGateOpenVpnManager.Middlewares;
using DataGateOpenVpnManager.Services.Interfaces;
using DataGateOpenVpnManager.Services.LocalIssuance;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace DataGateOpenVpnManager.Tests.Services.LocalIssuance;

public class ServiceClientLookupChannelTests
{
    private const string LookupPath = "/api/local/service-clients/by-virtual-ip/10.50.28.2";

    [Fact]
    public void DisabledByDefault() =>
        Assert.False(ServiceClientLookupOptions.FromConfiguration(new ConfigurationBuilder().Build()).Enabled);

    [Theory]
    [InlineData(false, "127.0.0.1", 404)] // disabled/public listener, including nginx on loopback
    [InlineData(false, "203.0.113.1", 404)]
    [InlineData(true, "203.0.113.1", 404)]
    [InlineData(true, "127.0.0.1", 200)]
    [InlineData(true, "::1", 200)]
    [InlineData(true, "::ffff:127.0.0.1", 200)]
    public async Task Middleware_OnlyDedicatedLoopbackConnectionCanBypassJwt(bool marked, string remote, int expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = LookupPath;
        context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        context.Request.Headers["X-Forwarded-For"] = "127.0.0.1";
        context.Request.Headers.Authorization = "Bearer irrelevant";
        if (marked)
        {
            var connection = new DefaultConnectionContext();
            await ServiceClientLookupChannel.MarkConnection(_ => Task.CompletedTask)(connection);
            context.Features.Set(connection.Features.Get<IConnectionItemsFeature>());
        }
        var validator = new Mock<IMicroserviceJwtValidator>(MockBehavior.Strict);
        var called = false;
        await new JwtValidationMiddleware(_ => { called = true; return Task.CompletedTask; }).Invoke(context, validator.Object);
        Assert.Equal(expected, context.Response.StatusCode);
        Assert.Equal(expected == 200, called);
        validator.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HttpRoute_DisabledIs404_EnabledDedicatedListenerNeedsNoJwt(bool enabled)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0, l =>
        {
            if (enabled)
                l.Use(ServiceClientLookupChannel.MarkConnection);
        }));
        var lookup = new Mock<IServiceClientLookup>();
        lookup.Setup(s => s.FindAsync("10.50.28.2", It.IsAny<CancellationToken>())).ReturnsAsync(
            new ServiceClientView("db1", "5E71A1", "10.50.28.2", "203.0.113.4:12000", DateTimeOffset.UnixEpoch));
        builder.Services.AddSingleton(lookup.Object);
        builder.Services.AddSingleton(Mock.Of<IMicroserviceJwtValidator>());
        builder.Services.AddControllers().AddApplicationPart(typeof(ServiceClientsController).Assembly).AddNewtonsoftJson();
        await using var app = builder.Build();
        app.UseMiddleware<JwtValidationMiddleware>();
        app.MapControllers();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        var response = await client.GetAsync(LookupPath);
        Assert.Equal(enabled ? HttpStatusCode.OK : HttpStatusCode.NotFound, response.StatusCode);
        if (enabled)
        {
            var body = Newtonsoft.Json.Linq.JObject.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(["commonName", "connectedSince", "realAddress", "serial", "virtualAddress"],
                body.Properties().Select(p => p.Name).OrderBy(n => n));
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/local-admin/profiles")).StatusCode);
        }
        lookup.Verify(s => s.FindAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), enabled ? Times.Once : Times.Never);
    }
}
