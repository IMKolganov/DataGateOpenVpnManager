using System.Reflection;
using DataGateCertManager.Configurations;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
Console.OutputEncoding = System.Text.Encoding.UTF8;
builder.Host.ConfigureSerilog();
var logger = Log.ForContext("SourceContext", "DILogger");
var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown version";
logger.Information($"Application version: {version};");

builder.Services.ConfigureServices();


var app = builder.Build();

app.ConfigureMiddleware();
app.ConfigurePipeline();

app.Run();