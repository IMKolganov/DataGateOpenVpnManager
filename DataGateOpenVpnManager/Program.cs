using DataGateOpenVpnManager.Configurations;
using DataGateOpenVpnManager.Services.LocalIssuance;

var builder = WebApplication.CreateBuilder(args);
Console.OutputEncoding = System.Text.Encoding.UTF8;
builder.Host.ConfigureSerilog();
builder.ConfigureExternalIpServices();

builder.Services.ConfigureServices(builder.Configuration);
builder.Services.ConfigureSignalR(builder.Configuration);
builder.ConfigureWebHost();

var app = builder.Build();
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});

var localIssuance = app.Services.GetRequiredService<LocalIssuanceOptions>();
if (localIssuance.Enabled)
    app.Lifetime.ApplicationStarted.Register(() => LocalAdminChannel.RestrictSocket(localIssuance));

app.ConfigureMiddleware();
app.ConfigurePipeline();

app.Run();
