using System.Net;
using DataGateOpenVpnManager.Services.Interfaces;

namespace DataGateOpenVpnManager.Middlewares;

public class JwtValidationMiddleware(RequestDelegate next)
{
    private static readonly string[] ExcludedPaths =
    {
        "/", "/favicon.ico", "/swagger", "/swagger/index.html", "/swagger/v1/swagger.json"
    };

    private static readonly string[] LocalOnlyPaths =
    {
        "/api/vpn-events/connect",
        "/api/vpn-events/disconnect",
        "/api/vpn-events/tlsverify",
        "/api/vpn-events/attempt",
        "/api/vpn-events/envdump"
    };

    public async Task Invoke(HttpContext context, IMicroserviceJwtValidator validator)
    {
        var requestPath = context.Request.Path;

        // Allow unauthenticated access to Swagger and other excluded paths
        if (ExcludedPaths.Any(p => requestPath.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        // Allow access to selected API paths from localhost without JWT
        var remoteIp = context.Connection.RemoteIpAddress;
        if (LocalOnlyPaths.Any(p => requestPath.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)) &&
            (remoteIp is not null && (IPAddress.IsLoopback(remoteIp) || remoteIp.ToString() == "::1")))
        {
            await next(context);
            return;
        }

        // Attempt to extract token from Authorization header
        string? token = null;
        var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
        if (authHeader?.StartsWith("Bearer ") == true)
        {
            token = authHeader.Substring("Bearer ".Length);
        }

        // Fallback: check query string for access_token (e.g., for WebSocket auth)
        if (string.IsNullOrWhiteSpace(token))
        {
            token = context.Request.Query["access_token"];
        }

        // Validate token
        if (!string.IsNullOrWhiteSpace(token) && validator.ValidateToken(token, out var principal))
        {
            context.User = principal ?? throw new InvalidOperationException("Principal is null");
            await next(context);
            return;
        }

        // Reject request if no valid token is found and not allowed by IP/path
        context.Response.StatusCode = 401;
        await context.Response.WriteAsync("Unauthorized");
    }
}