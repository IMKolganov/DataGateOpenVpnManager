using System.Net;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DataGateOpenVpnManager.Services.LocalIssuance;

/// <summary>Read-only loopback listener. A separate connection marker prevents public nginx proxy bypass.</summary>
public static class ServiceClientLookupChannel
{
    public const string PathPrefix = "/api/local/service-clients";
    private const string ConnectionMarker = "datagate.service-client-lookup";

    public static bool IsLookupPath(PathString path) =>
        path.StartsWithSegments(PathPrefix, StringComparison.OrdinalIgnoreCase);

    public static bool IsLookupConnection(HttpContext context) =>
        context.Features.Get<IConnectionItemsFeature>()?.Items.TryGetValue(ConnectionMarker, out var value) == true
        && value is true;

    public static bool IsAllowed(HttpContext context) =>
        IsLookupConnection(context)
        && context.Connection.RemoteIpAddress is { } ip
        && IPAddress.IsLoopback(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip);

    public static ConnectionDelegate MarkConnection(ConnectionDelegate next) => connection =>
    {
        connection.Items[ConnectionMarker] = true;
        return next(connection);
    };
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ServiceClientLookupOnlyAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (!ServiceClientLookupChannel.IsAllowed(context.HttpContext))
            context.Result = new NotFoundResult();
    }
}
