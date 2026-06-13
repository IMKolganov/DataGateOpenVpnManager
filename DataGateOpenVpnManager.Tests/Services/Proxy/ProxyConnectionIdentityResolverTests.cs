using System.Security.Claims;
using DataGateOpenVpnManager.Services.Proxy;
using Microsoft.AspNetCore.Http;

namespace DataGateOpenVpnManager.Tests.Services.Proxy;

public class ProxyConnectionIdentityResolverTests
{
    [Fact]
    public void Resolve_ReturnsNull_WhenNoIdentityData()
    {
        var resolver = new ProxyConnectionIdentityResolver();
        var context = new DefaultHttpContext();

        var result = resolver.Resolve(context, null);

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_UsesClientRefFromQuery_WhenProvided()
    {
        var resolver = new ProxyConnectionIdentityResolver();
        var context = new DefaultHttpContext();

        var result = resolver.Resolve(context, " app-client-1 ");

        Assert.NotNull(result);
        Assert.Equal("app-client-1", result!.ClientRef);
    }

    [Fact]
    public void Resolve_UsesClientRefFromHeader_WhenQueryMissing()
    {
        var resolver = new ProxyConnectionIdentityResolver();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Client-Ref"] = "hdr-client";

        var result = resolver.Resolve(context, null);

        Assert.NotNull(result);
        Assert.Equal("hdr-client", result!.ClientRef);
    }

    [Fact]
    public void Resolve_PrefersPrimaryClaims_InConfiguredOrder()
    {
        var resolver = new ProxyConnectionIdentityResolver();
        var context = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim("sub", "42"),
            new Claim(ClaimTypes.NameIdentifier, "1001"),
            new Claim("preferred_username", "preferred"),
            new Claim(ClaimTypes.Name, "primary-name"),
            new Claim("email", "user@example.com")
        };
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

        var result = resolver.Resolve(context, null);

        Assert.NotNull(result);
        Assert.Equal("1001", result!.UserId);
        Assert.Equal("primary-name", result.Username);
        Assert.Equal("user@example.com", result.Email);
    }

    [Fact]
    public void Resolve_TrimsWhitespace_FromAllIdentityFields()
    {
        var resolver = new ProxyConnectionIdentityResolver();
        var context = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim("uid", "  777 "),
            new Claim("username", "  john  "),
            new Claim(ClaimTypes.Email, "  john@example.com  ")
        };
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

        var result = resolver.Resolve(context, "  mobile-app  ");

        Assert.NotNull(result);
        Assert.Equal("mobile-app", result!.ClientRef);
        Assert.Equal("777", result.UserId);
        Assert.Equal("john", result.Username);
        Assert.Equal("john@example.com", result.Email);
    }
}
