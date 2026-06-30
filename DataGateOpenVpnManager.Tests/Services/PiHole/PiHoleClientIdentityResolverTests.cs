using DataGateOpenVpnManager.Services.PiHole;
using DataGateOpenVpnManager.Services.Proxy;

namespace DataGateOpenVpnManager.Tests.Services.PiHole;

public class PiHoleClientIdentityResolverTests
{
    [Fact]
    public void Enrich_MapsVirtualAddressToCommonName()
    {
        var snapshot = new OpenVpnManagementStatusSnapshot(
            DateTime.UtcNow,
            "raw",
            new[]
            {
                new OpenVpnManagementClientEntry(
                    "user-cert-1",
                    "203.0.113.1:443",
                    "10.51.30.5",
                    100,
                    200,
                    1719043200)
            },
            true);

        var resolver = new PiHoleClientIdentityResolver();
        var enriched = resolver.Enrich(
            new[]
            {
                new PiHoleQueryRecord(1, "10.51.30.5", "netflix.com", "A", "FORWARDED", DateTimeOffset.UtcNow)
            },
            snapshot);

        Assert.Single(enriched);
        Assert.Equal("user-cert-1", enriched[0].CommonName);
        Assert.Equal("netflix.com", enriched[0].Domain);
    }

    [Fact]
    public void Enrich_MapsVirtualAddressToCommonName_WhenRealAddressUsesOpenVpn27Format()
    {
        var snapshot = new OpenVpnManagementStatusSnapshot(
            DateTime.UtcNow,
            "raw",
            new[]
            {
                new OpenVpnManagementClientEntry(
                    "user-cert-27",
                    "tcp4-server:127.0.0.1:53188",
                    "10.51.30.5",
                    100,
                    200,
                    1719043200)
            },
            true);

        var resolver = new PiHoleClientIdentityResolver();
        var enriched = resolver.Enrich(
            new[]
            {
                new PiHoleQueryRecord(3, "10.51.30.5", "example.com", "A", "FORWARDED", DateTimeOffset.UtcNow)
            },
            snapshot);

        Assert.Single(enriched);
        Assert.Equal("user-cert-27", enriched[0].CommonName);
    }

    [Fact]
    public void Enrich_LeavesCommonNameNull_WhenClientUnknown()
    {
        var resolver = new PiHoleClientIdentityResolver();
        var enriched = resolver.Enrich(
            new[]
            {
                new PiHoleQueryRecord(2, "10.51.30.99", "unknown.test", null, "FORWARDED", DateTimeOffset.UtcNow)
            },
            null);

        Assert.Single(enriched);
        Assert.Null(enriched[0].CommonName);
    }
}
