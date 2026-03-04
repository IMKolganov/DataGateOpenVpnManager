namespace DataGateOpenVpnManager.Configurations;

public static class WebHostConfiguration
{
    public static void ConfigureWebHost(this WebApplicationBuilder builder)
    {
        var port = Environment.GetEnvironmentVariable("API_PORT") ?? "5010";

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(int.Parse(port));
        });
    }
}