using System.Security.Claims;

namespace DataGateOpenVpnManager.Services.Interfaces;

public interface IMicroserviceJwtValidator
{
    bool ValidateToken(string token, out ClaimsPrincipal? principal);
    Task InitAsync();
}
