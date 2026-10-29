using System.Text;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DataGateOpenVpnManager.Services.LocalIssuance;

/// <summary>
/// Admin channel for node-local issuance: a Unix domain socket in the private (0700, root) local-issuance
/// directory. Loopback is not enough — with network_mode: host any process on the server reaches 127.0.0.1,
/// and with OPENVPN_WSS_UDP_PROXY=rust every public request arrives at Kestrel from loopback via nginx.
/// Kestrel tags connections accepted on the socket; <c>/api/local-admin</c> answers 404 on any other listener.
/// </summary>
public static class LocalAdminChannel
{
    public const string PathPrefix = "/api/local-admin";
    private const string ConnectionMarker = "datagate.local-admin-socket";

    public static bool IsLocalAdminPath(PathString path) =>
        path.StartsWithSegments(PathPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>True only for requests that came in over the admin Unix socket.</summary>
    public static bool IsLocalAdminConnection(HttpContext context) =>
        context.Features.Get<IConnectionItemsFeature>()?.Items.TryGetValue(ConnectionMarker, out var value) == true
        && value is true;

    /// <summary>Kestrel connection middleware for the socket listener.</summary>
    public static ConnectionDelegate MarkConnection(ConnectionDelegate next) => connection =>
    {
        connection.Items[ConnectionMarker] = true;
        return next(connection);
    };

    /// <summary>Private directory + stale socket cleanup; call before Kestrel binds.</summary>
    public static void PrepareSocket(LocalIssuanceOptions options)
    {
        if (Encoding.UTF8.GetByteCount(options.SocketPath) > LocalIssuanceOptions.MaxUnixSocketPathBytes)
            throw new InvalidOperationException(
                $"{LocalIssuanceOptions.SocketKey} '{options.SocketPath}' is longer than {LocalIssuanceOptions.MaxUnixSocketPathBytes} bytes.");

        var socketDir = Path.GetDirectoryName(Path.GetFullPath(options.SocketPath))!;
        PrivateFiles.EnsurePrivateDirectory(options.Directory);
        PrivateFiles.EnsurePrivateDirectory(socketDir);
        if (File.Exists(options.SocketPath))
            File.Delete(options.SocketPath);
    }

    /// <summary>Kestrel creates the socket with the process umask; the 0700 parent already shields it.</summary>
    public static void RestrictSocket(LocalIssuanceOptions options)
    {
        if (File.Exists(options.SocketPath))
            PrivateFiles.RestrictToOwner(options.SocketPath);
    }
}

/// <summary>Defense in depth next to the middleware check: hide the action unless it came over the socket.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class LocalAdminOnlyAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (!LocalAdminChannel.IsLocalAdminConnection(context.HttpContext))
            context.Result = new NotFoundResult();
    }
}
