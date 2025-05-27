using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using DataGateCertManager.Services.Interfaces;
using Microsoft.IdentityModel.Tokens;

namespace DataGateCertManager.Services;

public class MicroserviceJwtValidator(HttpClient httpClient, ILogger<MicroserviceJwtValidator> logger)
    : IMicroserviceJwtValidator
{
    private readonly ILogger<MicroserviceJwtValidator> _logger = logger;
    private string? _publicKey;

    public async Task InitAsync()
    {
        const int delaySeconds = 5;

        while (true)
        {
            try
            {
                _logger.LogInformation("🔐 Attempt to fetch public key from backend...");
                _publicKey = await httpClient.GetStringAsync("api/Auth/public-key");

                _logger.LogInformation("✅ Successfully retrieved public key from backend.");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to get public key. Retrying in {Delay}s...", delaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
            }
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
                ValidAudience = "DataGateCertManager",

                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2)
            };

            var handler = new JwtSecurityTokenHandler();
            principal = handler.ValidateToken(token, validationParameters, out _);

            if (!principal.HasClaim(c => c is { Type: "purpose", Value: "cert-create" }))
                return false;

            if (!principal.HasClaim(c => c is { Type: ClaimTypes.Role, Value: "backend" }))
                return false;

            return true;
        }
        catch
        {
            principal = null;
            return false;
        }
    }
}
