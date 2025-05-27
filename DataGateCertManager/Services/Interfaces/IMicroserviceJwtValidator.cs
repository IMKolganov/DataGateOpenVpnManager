using System.Security.Claims;

namespace DataGateCertManager.Services.Interfaces;

public interface IMicroserviceJwtValidator
{
    bool ValidateToken(string token, out ClaimsPrincipal? principal);
    Task InitAsync();
}
