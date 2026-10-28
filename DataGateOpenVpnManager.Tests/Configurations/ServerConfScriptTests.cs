using System.Diagnostics;
using DataGateOpenVpnManager.Tests.TestSupport;

namespace DataGateOpenVpnManager.Tests.Configurations;

/// <summary>
/// scripts/server-conf.sh vs. golden files rendered from the pre-ccd entrypoint.sh heredoc:
/// a node without the new variables must get the exact same server.conf as before.
/// </summary>
public class ServerConfScriptTests
{
    private static readonly Dictionary<string, string> DefaultEnv = new()
    {
        ["PORT"] = "1194", ["PROTO"] = "udp", ["DEV_LINES"] = "dev tun", ["DCO_OPTION"] = "disable-dco",
        ["VPN_SUBNET"] = "10.51.28.0", ["VPN_NETMASK"] = "255.255.255.0", ["DNS1"] = "8.8.8.8", ["DNS2"] = "8.8.4.4",
        ["MSSFIX_LINE"] = "", ["TLS_VERSION_MIN"] = "1.2", ["CIPHER_LINES"] = "cipher AES-256-CBC", ["AUTH"] = "SHA256",
        ["EASYRSA_DIR"] = "/mnt/easy-rsa", ["DATA_DIR"] = "/mnt", ["OpenVpnManagement__Port"] = "5092",
        ["API_PORT"] = "5010", ["API_HOST"] = "127.0.0.1",
    };

    private static readonly Dictionary<string, string> DcoEnv = new()
    {
        ["PORT"] = "1295", ["PROTO"] = "tcp", ["DEV_LINES"] = "dev ovpn-udp\ndev-type tun", ["DCO_OPTION"] = "",
        ["VPN_SUBNET"] = "10.50.28.0", ["VPN_NETMASK"] = "255.255.255.0", ["DNS1"] = "1.1.1.1", ["DNS2"] = "9.9.9.9",
        ["MSSFIX_LINE"] = "push \"mssfix 1200\"", ["TLS_VERSION_MIN"] = "1.3",
        ["CIPHER_LINES"] = "data-ciphers AES-128-GCM:AES-256-GCM:CHACHA20-POLY1305\ncipher AES-128-GCM", ["AUTH"] = "SHA512",
        ["EASYRSA_DIR"] = "/openvpn-udp/easy-rsa", ["DATA_DIR"] = "/openvpn-udp", ["OpenVpnManagement__Port"] = "5095",
        ["API_PORT"] = "5009", ["API_HOST"] = "127.0.0.1",
    };

    private static string Render(Dictionary<string, string> env, params (string Key, string Value)[] extra)
    {
        var psi = new ProcessStartInfo("bash", ["-c", ". \"$1\" && render_server_conf", "bash", RepoPaths.Combine("scripts", "server-conf.sh")])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.Environment.Remove("CLIENT_CONFIG_DIR");
        psi.Environment.Remove("CCD_EXCLUSIVE");
        psi.Environment.Remove("STATIC_IP_RESERVED");
        foreach (var (k, v) in env)
            psi.Environment[k] = v;
        foreach (var (k, v) in extra)
            psi.Environment[k] = v;

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, stderr);
        return stdout;
    }

    private static string Golden(string name) =>
        File.ReadAllText(RepoPaths.Combine("DataGateOpenVpnManager.Tests", "Fixtures", "ServerConf", name));

    [Fact]
    public void WithoutNewVariables_DefaultNode_IsByteIdenticalToLegacy()
    {
        if (!OperatingSystem.IsLinux())
            return;
        Assert.Equal(Golden("default.server.conf"), Render(DefaultEnv));
    }

    [Fact]
    public void WithoutNewVariables_DcoTunDevMssfixNode_IsByteIdenticalToLegacy()
    {
        if (!OperatingSystem.IsLinux())
            return;
        Assert.Equal(Golden("dco-tundev-mssfix.server.conf"), Render(DcoEnv));
    }

    [Fact]
    public void CcdExclusiveAlone_DoesNothing()
    {
        if (!OperatingSystem.IsLinux())
            return;
        Assert.Equal(Golden("default.server.conf"), Render(DefaultEnv, ("CCD_EXCLUSIVE", "true")));
    }

    private const string LegacyServerLine = "server 10.51.28.0 255.255.255.0\n";

    // Default STATIC_IP_RESERVED=32 for 10.51.28.0/24: pool .2-.222, .223-.254 left for ifconfig-push.
    private const string ReservedServerLines =
        "server 10.51.28.0 255.255.255.0 nopool\nifconfig-pool 10.51.28.2 10.51.28.222 255.255.255.0\n";

    [Fact]
    public void ClientConfigDir_True_AppendsDataDirCcd_AndReservesTopOfSubnetFromPool()
    {
        if (!OperatingSystem.IsLinux())
            return;
        Assert.Equal(Golden("default.server.conf").Replace(LegacyServerLine, ReservedServerLines) + "\nclient-config-dir /mnt/ccd\n",
            Render(DefaultEnv, ("CLIENT_CONFIG_DIR", "true")));
    }

    [Fact]
    public void ClientConfigDir_PathWithExclusive_AppendsBothDirectives()
    {
        if (!OperatingSystem.IsLinux())
            return;
        Assert.Equal(Golden("default.server.conf").Replace(LegacyServerLine, ReservedServerLines) + "\nclient-config-dir /srv/ccd\nccd-exclusive\n",
            Render(DefaultEnv, ("CLIENT_CONFIG_DIR", "/srv/ccd/"), ("CCD_EXCLUSIVE", "true")));
    }

    [Fact]
    public void StaticIpReserved_Zero_KeepsServerPool()
    {
        if (!OperatingSystem.IsLinux())
            return;
        Assert.Equal(Golden("default.server.conf") + "\nclient-config-dir /mnt/ccd\n",
            Render(DefaultEnv, ("CLIENT_CONFIG_DIR", "true"), ("STATIC_IP_RESERVED", "0")));
    }

    [Fact]
    public void StaticIpReserved_WithoutClientConfigDir_DoesNothing()
    {
        if (!OperatingSystem.IsLinux())
            return;
        Assert.Equal(Golden("default.server.conf"), Render(DefaultEnv, ("STATIC_IP_RESERVED", "54")));
    }

    // The node validates static IPs with LocalIssuanceOptions/VpnSubnet; it must reserve exactly what the script does.
    [Theory]
    [InlineData("10.51.28.0", "255.255.255.0", "54")]
    [InlineData("10.51.28.0", "255.255.255.0", "")]
    [InlineData("10.8.0.0", "255.255.0.0", "1000")]
    [InlineData("10.51.28.0", "255.255.255.240", "")] // /28: 32 would eat the whole pool -> no reservation
    [InlineData("10.51.28.0", "255.255.255.0", "abc")]
    public void Script_And_Node_AgreeOnReservedRange(string subnet, string netmask, string reserved)
    {
        if (!OperatingSystem.IsLinux())
            return;
        var env = new Dictionary<string, string>(DefaultEnv) { ["VPN_SUBNET"] = subnet, ["VPN_NETMASK"] = netmask };
        var extra = new List<(string, string)> { ("CLIENT_CONFIG_DIR", "true") };
        if (reserved.Length > 0)
            extra.Add(("STATIC_IP_RESERVED", reserved));
        var conf = Render(env, [.. extra]);

        var count = DataGateOpenVpnManager.Services.LocalIssuance.LocalIssuanceOptions.ResolveStaticIpReserved(
            reserved.Length > 0 ? reserved : null, ccdEnabled: true, subnet, netmask);
        if (count == 0)
        {
            Assert.Contains($"server {subnet} {netmask}\n", conf);
            Assert.DoesNotContain("ifconfig-pool ", conf);
            return;
        }

        var vpn = DataGateOpenVpnManager.Helpers.VpnSubnet.Parse(subnet, netmask);
        var (first, _) = vpn.StaticReservedRange(count);
        var poolLine = conf.Split('\n').Single(l => l.StartsWith("ifconfig-pool ", StringComparison.Ordinal)).Split(' ');
        var poolEnd = System.Net.IPAddress.Parse(poolLine[2]);
        Assert.Contains($"server {subnet} {netmask} nopool\n", conf);
        Assert.False(vpn.IsInStaticReservedRange(poolEnd, count));
        var after = poolEnd.GetAddressBytes();
        after[3]++;
        Assert.Equal(first, new System.Net.IPAddress(after));
    }

    [Fact]
    public void Entrypoint_NoLongerInlinesServerConfHeredoc()
    {
        var entrypoint = File.ReadAllText(RepoPaths.Combine("entrypoint.sh"));
        Assert.DoesNotContain("cat <<EOF > \"$DATA_DIR/server.conf\"", entrypoint);
        Assert.Contains("render_server_conf > \"$DATA_DIR/server.conf\"", entrypoint);
    }
}
