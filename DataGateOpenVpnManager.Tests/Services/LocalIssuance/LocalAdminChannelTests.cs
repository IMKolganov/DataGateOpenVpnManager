using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using DataGateOpenVpnManager.Middlewares;
using DataGateOpenVpnManager.Services.Interfaces;
using DataGateOpenVpnManager.Services.LocalIssuance;
using DataGateOpenVpnManager.Tests.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace DataGateOpenVpnManager.Tests.Services.LocalIssuance;

/// <summary>
/// Real Kestrel with both listeners, as on a node: TCP (public API / loopback) and the admin Unix socket.
/// </summary>
public class LocalAdminChannelTests
{
    private static Mock<IMicroserviceJwtValidator> AcceptAnyJwt()
    {
        var validator = new Mock<IMicroserviceJwtValidator>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity("jwt"));
        validator.Setup(v => v.ValidateToken(It.IsAny<string>(), out principal, It.IsAny<JwtValidationRequestContext?>()))
            .Returns(true);
        return validator;
    }

    private static async Task<(WebApplication App, int TcpPort)> StartNodeAsync(LocalIssuanceOptions options)
    {
        LocalAdminChannel.PrepareSocket(options);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(AcceptAnyJwt().Object);
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.ListenUnixSocket(options.SocketPath, l =>
            {
                l.Protocols = HttpProtocols.Http1;
                l.Use(LocalAdminChannel.MarkConnection);
            });
            k.Listen(IPAddress.Loopback, 0);
        });

        var app = builder.Build();
        app.UseMiddleware<JwtValidationMiddleware>();
        app.MapGet("/api/local-admin/profiles", () => "local-admin-ok");
        app.MapGet("/api/info", () => "info-ok");
        await app.StartAsync();
        LocalAdminChannel.RestrictSocket(options);

        var address = app.Urls.Single(u => u.StartsWith("http://127.0.0.1", StringComparison.Ordinal));
        return (app, new Uri(address).Port);
    }

    private static HttpClient UnixSocketClient(string socketPath) => new(new SocketsHttpHandler
    {
        ConnectCallback = async (_, ct) =>
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
    }) { BaseAddress = new Uri("http://localhost") };

    [Fact]
    public async Task LocalAdminPath_OverTcpLoopback_Returns404_EvenWithValidJwt()
    {
        if (!OperatingSystem.IsLinux())
            return;

        using var dir = new TempDir();
        var options = new LocalIssuanceOptions { Enabled = true, Directory = dir.Combine("li"), SocketPath = dir.Combine("li", "admin.sock") };
        var (app, port) = await StartNodeAsync(options);
        await using var _ = app;

        using var tcp = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        Assert.Equal(HttpStatusCode.NotFound, (await tcp.GetAsync("/api/local-admin/profiles")).StatusCode);

        using var withJwt = new HttpRequestMessage(HttpMethod.Get, "/api/local-admin/profiles");
        withJwt.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "backend-jwt");
        Assert.Equal(HttpStatusCode.NotFound, (await tcp.SendAsync(withJwt)).StatusCode);

        // Loopback-only paths keep working as before over TCP.
        Assert.Equal("info-ok", await tcp.GetStringAsync("/api/info"));
    }

    [Fact]
    public async Task LocalAdminPath_OverAdminSocket_IsServedWithoutJwt()
    {
        if (!OperatingSystem.IsLinux())
            return;

        using var dir = new TempDir();
        var options = new LocalIssuanceOptions { Enabled = true, Directory = dir.Combine("li"), SocketPath = dir.Combine("li", "admin.sock") };
        var (app, _) = await StartNodeAsync(options);
        await using var __ = app;

        using var client = UnixSocketClient(options.SocketPath);
        Assert.Equal("local-admin-ok", await client.GetStringAsync("/api/local-admin/profiles"));
    }

    [Fact]
    public async Task AdminSocket_IsOwnerOnly_InsideOwnerOnlyDirectory()
    {
        if (!OperatingSystem.IsLinux())
            return;

        using var dir = new TempDir();
        var socketDir = dir.Combine("li");
        // Simulate entrypoint's "chmod -R a+rX $DATA_DIR" leaving the directory world-readable.
        Directory.CreateDirectory(socketDir);
        File.SetUnixFileMode(socketDir, (UnixFileMode)0b111_101_101);

        var options = new LocalIssuanceOptions { Enabled = true, Directory = socketDir, SocketPath = Path.Combine(socketDir, "admin.sock") };
        var (app, _) = await StartNodeAsync(options);
        await using var _ = app;

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(socketDir));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(options.SocketPath));
    }

    [Fact]
    public async Task Middleware_LocalAdminPath_WithoutSocketMarker_Returns404AndSkipsNext()
    {
        var nextCalled = false;
        var middleware = new JwtValidationMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/local-admin/profiles";
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers.Authorization = "Bearer valid";

        await middleware.Invoke(context, AcceptAnyJwt().Object);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public void PrepareSocket_RejectsPathLongerThanSunPath()
    {
        var longDir = "/tmp/" + new string('x', 120);
        var options = new LocalIssuanceOptions { Enabled = true, Directory = longDir, SocketPath = longDir + "/admin.sock" };

        Assert.Throws<InvalidOperationException>(() => LocalAdminChannel.PrepareSocket(options));
    }
}
