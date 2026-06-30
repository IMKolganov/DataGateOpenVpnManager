using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace DataGateOpenVpnManager.Services.Proxy;

public sealed class ProxyConnectionIdentityResolver : IProxyConnectionIdentityResolver
{
    public ProxyConnectionIdentity? Resolve(HttpContext context, string? clientRefFromQuery)
    {
        var clientRef = Normalize(clientRefFromQuery);
        if (string.IsNullOrWhiteSpace(clientRef))
            clientRef = Normalize(context.Request.Headers["X-Client-Ref"].FirstOrDefault());

        var user = context.User;
        var userId = ResolveClaim(user, ClaimTypes.NameIdentifier, "sub", "userId", "uid");
        var username = ResolveClaim(user, ClaimTypes.Name, "preferred_username", "unique_name", "username");
        var email = ResolveClaim(user, ClaimTypes.Email, "email");
        var userAgent = Normalize(context.Request.Headers.UserAgent.FirstOrDefault());

        if (string.IsNullOrWhiteSpace(userId) &&
            string.IsNullOrWhiteSpace(username) &&
            string.IsNullOrWhiteSpace(email) &&
            string.IsNullOrWhiteSpace(clientRef) &&
            string.IsNullOrWhiteSpace(userAgent))
            return null;

        return new ProxyConnectionIdentity
        {
            ClientRef = clientRef,
            UserId = userId,
            Username = username,
            Email = email,
            UserAgent = userAgent
        };
    }

    private static string? ResolveClaim(ClaimsPrincipal? principal, params string[] claimTypes)
    {
        if (principal is null)
            return null;

        foreach (var claimType in claimTypes)
        {
            var value = Normalize(principal.FindFirst(claimType)?.Value);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
