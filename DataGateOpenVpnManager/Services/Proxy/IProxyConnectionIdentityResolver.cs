using Microsoft.AspNetCore.Http;

namespace DataGateOpenVpnManager.Services.Proxy;

public interface IProxyConnectionIdentityResolver
{
    ProxyConnectionIdentity? Resolve(HttpContext context, string? clientRefFromQuery);
}
