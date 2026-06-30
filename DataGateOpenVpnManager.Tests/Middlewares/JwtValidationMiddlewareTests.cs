using System.Net;
using System.Security.Claims;
using DataGateOpenVpnManager.Middlewares;
using DataGateOpenVpnManager.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Moq;

namespace DataGateOpenVpnManager.Tests.Middlewares;

public class JwtValidationMiddlewareTests
{
    private static HttpContext CreateContext(string path, IPAddress? remoteIp = null, string? authHeader = null, string? queryToken = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = remoteIp;
        if (authHeader != null)
            context.Request.Headers["Authorization"] = authHeader;
        if (queryToken != null)
            context.Request.QueryString = new QueryString($"?access_token={Uri.EscapeDataString(queryToken)}");
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Fact]
    public async Task Invoke_WhenPathIsExcluded_CallsNext()
    {
        var nextCalled = false;
        RequestDelegate next = ctx => { nextCalled = true; return Task.CompletedTask; };
        var validatorMock = new Mock<IMicroserviceJwtValidator>();
        var middleware = new JwtValidationMiddleware(next);

        var context = CreateContext("/swagger/index.html", IPAddress.Loopback);
        await middleware.Invoke(context, validatorMock.Object);

        Assert.True(nextCalled);
        Assert.Equal(200, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/favicon.ico")]
    [InlineData("/swagger")]
    [InlineData("/api/proxy")]
    public async Task Invoke_WhenPathIsExcluded_AllowsRequest(string path)
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var validatorMock = new Mock<IMicroserviceJwtValidator>();
        var middleware = new JwtValidationMiddleware(next);

        var context = CreateContext(path);
        await middleware.Invoke(context, validatorMock.Object);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Invoke_WhenPathIsExcludedAndBearerTokenInvalid_PassesRequestContextToValidator()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        JwtValidationRequestContext? capturedContext = null;
        var validatorMock = new Mock<IMicroserviceJwtValidator>();
        validatorMock
            .Setup(v => v.ValidateToken(
                It.IsAny<string>(),
                out It.Ref<ClaimsPrincipal?>.IsAny,
                It.IsAny<JwtValidationRequestContext?>()))
            .Callback((string _, out ClaimsPrincipal? principal, JwtValidationRequestContext? ctx) =>
            {
                principal = null;
                capturedContext = ctx;
            })
            .Returns(false);
        var middleware = new JwtValidationMiddleware(next);

        var context = CreateContext(
            "/api/proxy/ws",
            IPAddress.Parse("10.20.30.40"),
            authHeader: "Bearer bad-token");
        context.Request.Headers["User-Agent"] = "ProbeBot/2";
        await middleware.Invoke(context, validatorMock.Object);

        Assert.True(nextCalled);
        Assert.NotNull(capturedContext);
        Assert.Equal("10.20.30.40", capturedContext!.RemoteIp);
        Assert.Equal("/api/proxy/ws", capturedContext.Path);
        Assert.Equal("GET", capturedContext.Method);
        Assert.Equal("ProbeBot/2", capturedContext.UserAgent);
    }

    [Fact]
    public async Task Invoke_WhenPathIsExcludedAndBearerTokenValid_AttachesPrincipal()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        ClaimsPrincipal? principal = new ClaimsPrincipal(new ClaimsIdentity("Test"));
        var validatorMock = new Mock<IMicroserviceJwtValidator>();
        validatorMock.Setup(v => v.ValidateToken("valid-token", out principal, It.IsAny<JwtValidationRequestContext?>())).Returns(true);
        var middleware = new JwtValidationMiddleware(next);

        var context = CreateContext("/api/proxy", authHeader: "Bearer valid-token");
        await middleware.Invoke(context, validatorMock.Object);

        Assert.True(nextCalled);
        Assert.Equal(principal, context.User);
    }

    [Theory]
    [InlineData("/api/info")]
    [InlineData("/api/diagnostics/proxy-audit")]
    [InlineData("/api/vpn-events/connect")]
    [InlineData("/api/vpn-events/disconnect")]
    public async Task Invoke_WhenLocalOnlyPathAndLoopback_CallsNext(string path)
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var validatorMock = new Mock<IMicroserviceJwtValidator>();
        var middleware = new JwtValidationMiddleware(next);

        var context = CreateContext(path, IPAddress.Loopback);
        await middleware.Invoke(context, validatorMock.Object);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Invoke_WhenLocalOnlyPathAndNonLoopback_Returns401()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var validatorMock = new Mock<IMicroserviceJwtValidator>();
        var middleware = new JwtValidationMiddleware(next);

        var context = CreateContext("/api/vpn-events/connect", IPAddress.Parse("192.168.1.1"));
        await middleware.Invoke(context, validatorMock.Object);

        Assert.Equal(401, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("Unauthorized", body);
    }

    [Fact]
    public async Task Invoke_WhenValidBearerToken_CallsNext()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var principal = new ClaimsPrincipal(new ClaimsIdentity("Test"));
        var validatorMock = new Mock<IMicroserviceJwtValidator>();
        validatorMock.Setup(v => v.ValidateToken(It.IsAny<string>(), out principal, It.IsAny<JwtValidationRequestContext?>())).Returns(true);

        var middleware = new JwtValidationMiddleware(next);
        var context = CreateContext("/api/certs/get-all", IPAddress.Parse("10.0.0.1"), "Bearer valid-token");
        await middleware.Invoke(context, validatorMock.Object);

        Assert.True(nextCalled);
        Assert.Equal(principal, context.User);
    }

    [Fact]
    public async Task Invoke_WhenNoTokenAndNotExcluded_Returns401()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var validatorMock = new Mock<IMicroserviceJwtValidator>();
        var middleware = new JwtValidationMiddleware(next);

        var context = CreateContext("/api/certs/get-all", IPAddress.Parse("10.0.0.1"));
        await middleware.Invoke(context, validatorMock.Object);

        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task Invoke_WhenQueryAccessTokenValid_CallsNext()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        ClaimsPrincipal? principal = new ClaimsPrincipal(new ClaimsIdentity("Test"));
        var validatorMock = new Mock<IMicroserviceJwtValidator>();
        validatorMock.Setup(v => v.ValidateToken("query-token", out principal, It.IsAny<JwtValidationRequestContext?>())).Returns(true);

        var middleware = new JwtValidationMiddleware(next);
        var context = CreateContext("/api/info", null, null, "query-token");
        await middleware.Invoke(context, validatorMock.Object);

        Assert.True(nextCalled);
    }
}
