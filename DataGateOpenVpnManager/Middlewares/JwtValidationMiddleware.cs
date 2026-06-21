using System.Net;
using DataGateOpenVpnManager.Services.Interfaces;

namespace DataGateOpenVpnManager.Middlewares;

public class JwtValidationMiddleware(RequestDelegate next)
{
    private static readonly string[] ExcludedPaths =
    {
        "/", "/favicon.ico", "/swagger", "/swagger/index.html", "/swagger/v1/swagger.json", "/api/proxy"
    };

    private static readonly string[] LocalOnlyPaths =
    {
        "/api/info",
        "/api/diagnostics",
        "/api/vpn-events/connect",
        "/api/vpn-events/disconnect",
        "/api/vpn-events/tlsverify",
        "/api/vpn-events/attempt",
        "/api/vpn-events/envdump"
    };

    public async Task Invoke(HttpContext context, IMicroserviceJwtValidator validator)
    {
        var requestPath = context.Request.Path;
        var token = ExtractToken(context);

        // Allow unauthenticated access to Swagger and other excluded paths
        if (ExcludedPaths.Any(p => requestPath.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
        {
            // For excluded paths we still attach principal when token is provided,
            // so endpoints like /api/proxy can enrich telemetry with user identity.
            if (!string.IsNullOrWhiteSpace(token) && validator.ValidateToken(token, out var excludedPrincipal))
                context.User = excludedPrincipal ?? throw new InvalidOperationException("Principal is null");

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

    private static string? ExtractToken(HttpContext context)
    {
        var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
        if (authHeader?.StartsWith("Bearer ") == true)
            return authHeader.Substring("Bearer ".Length);

        var token = context.Request.Query["access_token"];
        return string.IsNullOrWhiteSpace(token) ? null : token.ToString();
    }
}