using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DataGateMonitor.Serialization;
using DataGateMonitor.SharedModels.Responses;
using DataGateOpenVpnManager.Services;
using DataGateOpenVpnManager.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;

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
