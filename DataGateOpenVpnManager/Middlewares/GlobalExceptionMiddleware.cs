using System.Net;
using Newtonsoft.Json;

namespace DataGateOpenVpnManager.Middlewares;

public class GlobalExceptionMiddleware(
    RequestDelegate next,
    IServiceProvider serviceProvider,
    ILogger<GlobalExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception occurred.");
            using var scope = serviceProvider.CreateScope();
            await HandleExceptionAsync(context, ex);

        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        if (context.Response.HasStarted)
        {
            return Task.CompletedTask;
        }

        context.Response.Clear();
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

        var response = new
        {
            context.Response.StatusCode,
            Message = "An unexpected error occurred. Please try again later.",
            Detail = exception.Message
        };

        var json = JsonConvert.SerializeObject(response);
        return context.Response.WriteAsync(json);
    }
}