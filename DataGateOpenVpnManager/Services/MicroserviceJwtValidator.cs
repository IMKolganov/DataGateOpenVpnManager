using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using DataGateOpenVpnManager.Services.Interfaces;
using Microsoft.IdentityModel.Tokens;
using OpenVPNGateMonitor.SharedModels.Responses;

namespace DataGateOpenVpnManager.Services;

public class MicroserviceJwtValidator(HttpClient httpClient, ILogger<MicroserviceJwtValidator> logger)
    : IMicroserviceJwtValidator
{
    private string? _publicKey;

    public async Task InitAsync()
    {
        const int delaySeconds = 5;
        var random = new Random();

        while (true)
        {
            int pin = random.Next(10001, 99999);

            try
            {
                logger.LogInformation("🔐 Attempt to fetch public key from backend with pin {Pin}...", pin);

                var response = await httpClient.GetFromJsonAsync<ApiResponse<string>>($"api/Auth/public-key/{pin}");

                if (response is { Success: true, Data: not null })
                {
                    _publicKey = response.Data;
                    logger.LogInformation("✅ Successfully retrieved public key from backend.");
                    return;
                }

                logger.LogWarning("⚠️ Backend responded with error: {Message}", response?.Message ?? "Unknown error");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "⚠️ Failed to get public key with pin {Pin}. Retrying in {Delay}s...", pin, delaySeconds);
            }

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
        }
    }

    public bool ValidateToken(string token, out ClaimsPrincipal? principal)
    {
        principal = null;

        try
        {
            var rsa = RSA.Create();
            if (string.IsNullOrEmpty(_publicKey))
            {
                throw new InvalidOperationException("Public key is empty");
            }
            rsa.ImportFromPem(_publicKey.ToCharArray());

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new RsaSecurityKey(rsa),
                ValidateIssuer = true,
                ValidIssuer = "OpenVPNGateBackend",

                ValidateAudience = true,
                ValidAudience = "DataGateOpenVpnManager",

                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2)
            };

            var handler = new JwtSecurityTokenHandler();
            principal = handler.ValidateToken(token, validationParameters, out _);

            if (!principal.HasClaim(c => c is { Type: "purpose", Value: "cert-create" }))
                throw new Exception("Missing required claim: purpose=cert-create");

            if (!principal.HasClaim(c => c is { Type: ClaimTypes.Role, Value: "backend" }))
                throw new Exception("Missing required role claim: backend");

            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning("❌ Token validation error: {Message}", ex.Message);
            principal = null;
            return false;
        }
    }
}
