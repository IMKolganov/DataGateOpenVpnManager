using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using DataGateCertManager.Services.Interfaces;
using Microsoft.IdentityModel.Tokens;

namespace DataGateCertManager.Services;

public class MicroserviceJwtValidator : IMicroserviceJwtValidator
{
    private readonly IConfiguration _config;
    private readonly ILogger<MicroserviceJwtValidator> _logger;
    private readonly string _publicKey;

    public MicroserviceJwtValidator(IConfiguration config, ILogger<MicroserviceJwtValidator> logger)
    {
        _config = config;
        _logger = logger;

        var backendUrl = _config["Backend:PublicKeyEndpoint"];
        _publicKey = new HttpClient().GetStringAsync(backendUrl).Result;
    }

    public bool ValidateToken(string token, out ClaimsPrincipal? principal)
    {
        principal = null;

        try
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(_publicKey.ToCharArray());

            var parameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new RsaSecurityKey(rsa),
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            };

            var handler = new JwtSecurityTokenHandler();
            principal = handler.ValidateToken(token, parameters, out _);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate JWT");
            return false;
        }
    }
}
