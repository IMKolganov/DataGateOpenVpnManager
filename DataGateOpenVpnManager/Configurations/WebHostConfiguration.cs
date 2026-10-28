using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Net;
using DataGateOpenVpnManager.Services.LocalIssuance;

namespace DataGateOpenVpnManager.Configurations;

public static class WebHostConfiguration
{
    public static void ConfigureWebHost(this WebApplicationBuilder builder)
    {
        builder.WebHost.UseSockets(options =>
        {
            // Default write buffer is 64 KiB — UDP→WS batches wait on TCP ACKs under load.
            options.IOQueueCount = Math.Max(1, Environment.ProcessorCount);
            options.MaxWriteBufferSize = 1024 * 1024;
            options.MaxReadBufferSize = 1024 * 1024;
        });

        var localIssuance = LocalIssuanceOptions.FromConfiguration(builder.Configuration);
        if (localIssuance.Enabled)
            LocalAdminChannel.PrepareSocket(localIssuance);

        builder.WebHost.ConfigureKestrel(options =>
        {
            if (localIssuance.Enabled)
            {
                // Node-local admin channel (root-only Unix socket); see LocalAdminChannel.
                options.ListenUnixSocket(localIssuance.SocketPath, listen =>
                {
                    listen.Protocols = HttpProtocols.Http1;
                    listen.Use(LocalAdminChannel.MarkConnection);
                });
            }

            var proxyMode = (Environment.GetEnvironmentVariable("OPENVPN_WSS_UDP_PROXY") ?? "dotnet")
                .Trim()
                .ToLowerInvariant();

            if (proxyMode == "rust")
            {
                // Nginx front owns public API_PORT; Kestrel stays on loopback.
                var internalPort = int.Parse(
                    Environment.GetEnvironmentVariable("INTERNAL_DOTNET_PORT") ?? "18080");
                options.Listen(IPAddress.Loopback, internalPort, listen =>
                {
                    listen.Protocols = HttpProtocols.Http1;
                });
                return;
            }

            var port = int.Parse(Environment.GetEnvironmentVariable("API_PORT") ?? "5010");
            options.ListenAnyIP(port, listen =>
            {
                listen.Protocols = HttpProtocols.Http1;
            });
        });
    }
}
