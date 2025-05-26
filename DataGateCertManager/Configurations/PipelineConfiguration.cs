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

        app.MapGet("/", (IConfiguration config) => Results.Json(new
        {
            version,
            environment = environmentName,
            application = "DataGateCertManager",
            description = "This service manages OpenVPN certificates and provides a JSON API for operations like create/revoke.",
            config = new
            {
                easyRsaPath = config["EASY_RSA_PATH"],
                dataDir = config["DATA_DIR"],
                port = config["PORT"],
                apiPort = config["API_PORT"],
                proto = config["PROTO"],
                mgmtPort = config["MGMT_PORT"]
            }
        }));
        app.Logger.LogInformation($"Application version: {version}; Environment: {environmentName};");
    }
}