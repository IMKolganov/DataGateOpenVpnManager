using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DataGateMonitor.Serialization;
using DataGateMonitor.SharedModels.Responses;
using DataGateOpenVpnManager.Services;
using DataGateOpenVpnManager.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Moq.Protected;

namespace DataGateOpenVpnManager.Tests.Services;

public class MicroserviceJwtValidatorTests
{
    [Fact]
    public void ValidateToken_ReturnsTrue_ForValidBackendToken()
    {
        using var rsa = RSA.Create(2048);
        var validator = CreateValidatorWithPublicKey(rsa);
        var token = CreateToken(rsa);

        var valid = validator.ValidateToken(token, out var principal);

        Assert.True(valid);
        Assert.NotNull(principal);
        Assert.Equal("backend", principal!.FindFirst(ClaimTypes.Role)?.Value);
    }

    [Fact]
    public void ValidateToken_ReturnsFalse_WhenPurposeClaimMissing()
    {
        using var rsa = RSA.Create(2048);
        var validator = CreateValidatorWithPublicKey(rsa);
        var token = CreateToken(rsa, includePurpose: false);

        Assert.False(validator.ValidateToken(token, out var principal));
        Assert.Null(principal);
    }

    [Fact]
    public async Task ValidateToken_WhenInvalid_IncludesCallerContextInLog()
    {
        using var rsa = RSA.Create(2048);
        var publicPem = new string(PemEncoding.Write("PUBLIC KEY", rsa.ExportSubjectPublicKeyInfo()));

        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(
                    $$"""{"success":true,"data":"{{publicPem.Replace("\n", "\\n")}}"}""")
            });

        var httpClient = new HttpClient(mockHandler.Object) { BaseAddress = new Uri("http://backend/") };
        var logger = new TestLogger<MicroserviceJwtValidator>();
        var validator = new MicroserviceJwtValidator(httpClient, logger);
        await validator.InitAsync();

        var ok = validator.ValidateToken(
            "not-a-jwt",
            out _,
            new JwtValidationRequestContext("203.0.113.9", "/api/proxy/ws", "GET", "TestAgent/1.0"));

        Assert.False(ok);
        Assert.Contains(logger.Messages, m =>
            m.Contains("203.0.113.9")
            && m.Contains("GET")
            && m.Contains("/api/proxy/ws")
            && m.Contains("TestAgent/1.0"));
    }

    [Fact]
    public async Task InitAsync_LoadsPublicKeyFromBackend()
    {
        using var rsa = RSA.Create(2048);
        var publicPem = rsa.ExportRSAPublicKeyPem();
        var handler = new StubHttpMessageHandler(_ =>
        {
            var json = ProjectJson.Serialize(ApiResponse<string>.SuccessResponse(publicPem));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var validator = new MicroserviceJwtValidator(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:9999/") },
            NullLogger<MicroserviceJwtValidator>.Instance);
        await validator.InitAsync();

        var token = CreateToken(rsa);
        Assert.True(validator.ValidateToken(token, out _));
    }

    private static MicroserviceJwtValidator CreateValidatorWithPublicKey(RSA rsa)
    {
        var validator = new MicroserviceJwtValidator(new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage())), NullLogger<MicroserviceJwtValidator>.Instance);
        typeof(MicroserviceJwtValidator)
            .GetField("_publicKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(validator, rsa.ExportRSAPublicKeyPem());
        return validator;
    }

    private static string CreateToken(RSA rsa, bool includePurpose = true)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, "backend") };
        if (includePurpose)
            claims.Add(new Claim("purpose", "cert-create"));

        var token = new JwtSecurityToken(
            issuer: "OpenVPNGateBackend",
            audience: "DataGateOpenVpnManager",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}

internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(responder(request));
}

public class MicroserviceJwtValidatorInitializerTests
{
    [Fact]
    public async Task StartAsync_CallsInitOnConcreteValidator()
    {
        using var rsa = RSA.Create(2048);
        var handler = new StubHttpMessageHandler(_ =>
        {
            var json = ProjectJson.Serialize(ApiResponse<string>.SuccessResponse(rsa.ExportRSAPublicKeyPem()));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var validator = new MicroserviceJwtValidator(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:9999/") }, NullLogger<MicroserviceJwtValidator>.Instance);
        var initializer = new MicroserviceJwtValidatorInitializer(validator);
        await initializer.StartAsync(CancellationToken.None);

        var token = CreateSmokeToken(rsa);
        Assert.True(validator.ValidateToken(token, out _));
    }

    [Fact]
    public async Task StopAsync_Completes()
    {
        var initializer = new MicroserviceJwtValidatorInitializer(Mock.Of<IMicroserviceJwtValidator>());
        await initializer.StopAsync(CancellationToken.None);
    }

    private static string CreateSmokeToken(RSA rsa)
    {
        var token = new JwtSecurityToken(
            issuer: "OpenVPNGateBackend",
            audience: "DataGateOpenVpnManager",
            claims:
            [
                new Claim("purpose", "cert-create"),
                new Claim(ClaimTypes.Role, "backend")
            ],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
