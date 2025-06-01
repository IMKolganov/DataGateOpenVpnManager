using DataGateCertManager.Services.Interfaces;

namespace DataGateCertManager.Middlewares;

public class JwtValidationMiddleware(RequestDelegate next)
{
    private static readonly string[] ExcludedPaths =
    {
        "/",
        "/swagger",
        "/swagger/index.html",
        "/swagger/v1/swagger.json"
    };

    public async Task Invoke(HttpContext context, IMicroserviceJwtValidator validator)
    {
        // Skip token validation for Swagger and static files under /swagger
        if (ExcludedPaths.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        string? token = null;

        // Try to get token from Authorization header
        var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
        if (authHeader?.StartsWith("Bearer ") == true)
        {
            token = authHeader.Substring("Bearer ".Length);
        }

        // Fallback to access_token query param (e.g., for SignalR WebSocket auth)
        if (string.IsNullOrWhiteSpace(token))
        {
            token = context.Request.Query["access_token"];
        }

        if (!string.IsNullOrWhiteSpace(token) && validator.ValidateToken(token, out var principal))
        {
            context.User = principal ?? throw new InvalidOperationException("Principal is null");
            await next(context);
            return;
        }

        context.Response.StatusCode = 401;
        await context.Response.WriteAsync("Unauthorized");
    }
}