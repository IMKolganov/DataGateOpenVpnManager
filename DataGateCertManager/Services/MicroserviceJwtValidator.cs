using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using DataGateCertManager.Services.Interfaces;
using Microsoft.IdentityModel.Tokens;

namespace DataGateCertManager.Services;

public class MicroserviceJwtValidator : IMicroserviceJwtValidator
{
    private readonly ILogger<MicroserviceJwtValidator> _logger;
    private readonly string _publicKey;

    public MicroserviceJwtValidator(IConfiguration config, ILogger<MicroserviceJwtValidator> logger)
    {
        _logger = logger;

        var backendUrl = config["Backend:PublicKeyEndpoint"];
        _publicKey = new HttpClient().GetStringAsync(backendUrl).Result;
    }

    public bool ValidateToken(string token, out ClaimsPrincipal? principal)
    {
        principal = null;

        try
        {
            var rsa = RSA.Create();
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
