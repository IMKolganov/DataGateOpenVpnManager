using DataGateCertManager.Services.Interfaces;

namespace DataGateCertManager.Middlewares;

public class JwtValidationMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext context, IMicroserviceJwtValidator validator)
    {
        var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
        if (authHeader?.StartsWith("Bearer ") == true)
        {
            var token = authHeader.Substring("Bearer ".Length);
            if (validator.ValidateToken(token, out var principal))
            {
                context.User = principal ?? throw new InvalidOperationException("Principal is null");
                await next(context);
                return;
            }
        }

        context.Response.StatusCode = 401;
        await context.Response.WriteAsync("Unauthorized");
    }
}
