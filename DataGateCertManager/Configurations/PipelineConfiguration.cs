using System.Reflection;

namespace DataGateCertManager.Configurations;

public static class PipelineConfiguration
{
    public static void ConfigurePipeline(this WebApplication app)
    {
        app.UseStaticFiles();
        if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "DataGateCertManager API V1");
                options.HeadContent = @"<link rel=""icon"" type=""image/png"" href=""/favicon.ico"">";
            });
        }

        app.UseHttpsRedirection();
        app.UseAuthorization();
        app.MapControllers();
        
        app.UseStatusCodePagesWithReExecute("/error/{0}");
        app.MapGet("/error/404", () => Results.Problem(statusCode: 404, title: "Page Not Found", 
                detail: "The requested resource was not found."))
            .ExcludeFromDescription();

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown version";
        var environmentName = app.Environment.EnvironmentName;
        
        app.MapGet("/",
            () => Results.Json(new
            {
                version = version,
                environment = environmentName,
                application = "DataGateCertManager"
            }));

        app.Logger.LogInformation($"Application version: {version}; Environment: {environmentName};");
    }
}