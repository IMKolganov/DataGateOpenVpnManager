using System.Reflection;
using DataGateOpenVpnManager.Hubs;

namespace DataGateOpenVpnManager.Configurations;

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
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "DataGateOpenVpnManager API V1");
                options.HeadContent = @"<link rel=""icon"" type=""image/png"" href=""/favicon.ico"">";
            });
        }
        
        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(20)
        });

        app.UseAuthorization();
        app.MapControllers();
        
        app.UseStatusCodePagesWithReExecute("/error/{0}");
        app.MapGet("/error/401", () => Results.Problem(statusCode: 401, title: "Unauthorized",
                detail: "Authentication is required and has failed or has not yet been provided."))
            .ExcludeFromDescription();
        app.MapGet("/error/403", () => Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "You do not have permission to access this resource."))
            .ExcludeFromDescription();
        app.MapGet("/error/404", () => Results.Problem(statusCode: 404, title: "Page Not Found",
                detail: "The requested resource was not found."))
            .ExcludeFromDescription();

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown version";
        var environmentName = app.Environment.EnvironmentName;

        app.MapGet("/", (IConfiguration config) => Results.Json(new
        {
            version,
            environment = environmentName,
            application = "DataGateOpenVpnManager",
            description = "This service manages OpenVPN certificates and provides a JSON API for operations like create/revoke.",
            config = new
            {
                dns1 = config["DNS1"],
                dns2 = config["DNS2"],
                vpnSubnet = config["VPN_SUBNET"],
                vpnNetmask = config["VPN_NETMASK"],
                easyRsaPath = config["EASY_RSA_PATH"],
                dataDir = config["DATA_DIR"],
                port = config["PORT"],
                apiPort = config["API_PORT"],
                proto = config["PROTO"],
                openVpnManagement = new
                {
                    host = config["OpenVpnManagement:Host"],
                    port = config["OpenVpnManagement:Port"]
                },
                backendBaseUrl = config["BACKEND__BASEURL"]
            }
        }));
        app.Logger.LogInformation($"Application version: {version}; Environment: {environmentName};");
        
        //SignalR
        app.MapHub<OpenVpnSignalHub>("/hubs/openvpn");
        app.MapHub<OpenVpnEventHub>("/hubs/openvpn-event");
    }
}