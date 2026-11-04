using DataGateOpenVpnManager.Helpers;

namespace DataGateOpenVpnManager.Tests.Helpers;

public class OvpnServiceProfileTemplateTests
{
    private const string Ovpn = """
        setenv FRIENDLY_NAME "db1"
        client
        dev tun
        proto udp
        remote 203.0.113.10 1194
        redirect-gateway def1
        dhcp-option DNS 8.8.8.8
        block-outside-dns
        nobind
        verb 3
        <ca>
        -----BEGIN CERTIFICATE-----
        dhcp-option inside-pem-must-stay
        -----END CERTIFICATE-----
        </ca>
        """;

    [Fact]
    public void Apply_AddsPullFiltersOnceRightAfterClient()
    {
        var lines = OvpnServiceProfileTemplate.Apply(Ovpn).Split('\n');

        var client = Array.IndexOf(lines, "client");
        Assert.Equal("pull-filter ignore \"redirect-gateway\"", lines[client + 1]);
        Assert.Equal("pull-filter ignore \"dhcp-option\"", lines[client + 2]);
        Assert.Equal("pull-filter ignore \"block-outside-dns\"", lines[client + 3]);
        Assert.Equal(3, lines.Count(l => l.StartsWith("pull-filter", StringComparison.Ordinal)));
    }

    [Fact]
    public void Apply_DropsLocalGatewayAndDnsDirectives_ButNotInlineBlocks()
    {
        var result = OvpnServiceProfileTemplate.Apply(Ovpn);
        var lines = result.Split('\n');

        Assert.DoesNotContain(lines, l => l.StartsWith("redirect-gateway", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.StartsWith("dhcp-option DNS", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l == "block-outside-dns");
        Assert.Contains("dhcp-option inside-pem-must-stay", lines);
        Assert.Contains("remote 203.0.113.10 1194", lines);
        // Nothing else added: original line count - 3 removed + 3 filters.
        Assert.Equal(Ovpn.Split('\n').Length, lines.Length);
    }

    [Fact]
    public void Apply_IsIdempotent()
    {
        var once = OvpnServiceProfileTemplate.Apply(Ovpn);
        Assert.Equal(once, OvpnServiceProfileTemplate.Apply(once));
    }

    [Fact]
    public void Apply_WithoutClientDirective_PrependsFilters()
    {
        var result = OvpnServiceProfileTemplate.Apply("dev tun\nremote x 1");
        Assert.StartsWith("pull-filter ignore \"redirect-gateway\"\n", result);
    }
}
