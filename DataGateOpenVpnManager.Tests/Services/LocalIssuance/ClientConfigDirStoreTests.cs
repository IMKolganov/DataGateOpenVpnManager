using DataGateOpenVpnManager.Services.LocalIssuance;
using DataGateOpenVpnManager.Services.Proxy;
using DataGateOpenVpnManager.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace DataGateOpenVpnManager.Tests.Services.LocalIssuance;

public class ClientConfigDirStoreTests : IDisposable
{
    private readonly TempDir _dir = new();

    private ClientConfigDirStore CreateStore(bool enabled = true, bool exclusive = false,
        IOpenVpnManagementStatusCache? managementStatus = null) =>
        new(new LocalIssuanceOptions
        {
            ClientConfigDir = enabled ? _dir.Combine("ccd") : null,
            CcdExclusive = exclusive,
            VpnSubnet = "10.50.28.0",
            VpnNetmask = "255.255.255.0",
            IppFile = _dir.Combine("ipp.txt"),
        }, NullLogger<ClientConfigDirStore>.Instance, managementStatus);

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task WriteAsync_WritesIfconfigPushWithSubnetMask()
    {
        var store = CreateStore();

        await store.WriteAsync("db1", "10.50.28.201", CancellationToken.None);

        var lines = (await File.ReadAllTextAsync(_dir.Combine("ccd", "db1"))).TrimEnd('\n').Split('\n');
        Assert.Equal(ClientConfigDirStore.ManagedHeader, lines[0]);
        Assert.Equal("ifconfig-push 10.50.28.201 255.255.255.0", lines[1]);
        Assert.Equal(2, lines.Length);
        Assert.Equal("10.50.28.201", store.ReadStaticAssignments()["db1"]);
    }

    [Fact]
    public async Task WriteAsync_WithoutStaticIp_WritesHeaderOnly_ForCcdExclusive()
    {
        var store = CreateStore(exclusive: true);

        await store.WriteAsync("laptop", null, CancellationToken.None);

        Assert.Equal(ClientConfigDirStore.ManagedHeader + "\n", await File.ReadAllTextAsync(_dir.Combine("ccd", "laptop")));
        Assert.Empty(store.ReadStaticAssignments());
    }

    [Theory]
    [InlineData("10.50.29.5", "outside")]
    [InlineData("10.50.28.0", "network address")]
    [InlineData("10.50.28.255", "broadcast")]
    [InlineData("10.50.28.1", "server address")]
    [InlineData("not-an-ip", "valid IPv4")]
    [InlineData("10.50.28", "valid IPv4")]
    public async Task ValidateStaticIp_RejectsUnusableAddresses(string ip, string expected)
    {
        var ex = await Assert.ThrowsAsync<LocalIssuanceValidationException>(() => CreateStore().ValidateStaticIpAsync("db1", ip, CancellationToken.None));
        Assert.Contains(expected, ex.Message);
        Assert.False(ex.IsConflict);
    }

    [Fact]
    public async Task ValidateStaticIp_RejectsAddressPinnedToAnotherCn()
    {
        var store = CreateStore();
        await store.WriteAsync("db1", "10.50.28.201", CancellationToken.None);

        var ex = await Assert.ThrowsAsync<LocalIssuanceValidationException>(() => store.ValidateStaticIpAsync("db2", "10.50.28.201", CancellationToken.None));

        Assert.True(ex.IsConflict);
        Assert.Contains("db1", ex.Message);
        await store.ValidateStaticIpAsync("db1", "10.50.28.201", CancellationToken.None); // re-issuing the same CN keeps its address
    }

    [Fact]
    public async Task ValidateStaticIp_RejectsAddressHeldInDynamicPool()
    {
        File.WriteAllText(_dir.Combine("ipp.txt"), "laptop,10.50.28.7,\n");

        var ex = await Assert.ThrowsAsync<LocalIssuanceValidationException>(() => CreateStore().ValidateStaticIpAsync("db1", "10.50.28.7", CancellationToken.None));

        Assert.True(ex.IsConflict);
        Assert.Contains("laptop", ex.Message);
    }

    /// <summary>Management status that answers like <c>status 3</c> with these connected clients.</summary>
    private sealed class FakeManagementStatus(bool reachable, params (string Cn, string Ip)[] clients) : IOpenVpnManagementStatusCache
    {
        private OpenVpnManagementStatusSnapshot? _snapshot;

        public OpenVpnManagementStatusSnapshot? GetSnapshot() => _snapshot;

        public Task RefreshAsync(CancellationToken cancellationToken)
        {
            if (reachable)
                _snapshot = new OpenVpnManagementStatusSnapshot(DateTime.UtcNow, "", clients
                    .Select(c => new OpenVpnManagementClientEntry(c.Cn, "198.51.100.7:50000", c.Ip, 0, 0, 0)).ToList(), true);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ValidateStaticIp_RejectsAddressOfConnectedClient_NotYetInIppTxt()
    {
        var store = CreateStore(managementStatus: new FakeManagementStatus(true, ("alice", "10.50.28.2")));

        var ex = await Assert.ThrowsAsync<LocalIssuanceValidationException>(() =>
            store.ValidateStaticIpAsync("db1", "10.50.28.2", CancellationToken.None));

        Assert.True(ex.IsConflict);
        Assert.Contains("alice", ex.Message);
        await store.ValidateStaticIpAsync("alice", "10.50.28.2", CancellationToken.None); // its own address
        await store.ValidateStaticIpAsync("db1", "10.50.28.3", CancellationToken.None);
    }

    [Fact]
    public async Task ValidateStaticIp_ManagementUnreachable_FallsBackToCcdAndIppTxt()
    {
        var store = CreateStore(managementStatus: new FakeManagementStatus(false, ("alice", "10.50.28.2")));

        await store.ValidateStaticIpAsync("db1", "10.50.28.2", CancellationToken.None);
    }

    [Fact]
    public async Task ValidateStaticIp_WhenCcdDisabled_ExplainsHowToEnable()
    {
        var ex = await Assert.ThrowsAsync<LocalIssuanceValidationException>(() => CreateStore(enabled: false).ValidateStaticIpAsync("db1", "10.50.28.201", CancellationToken.None));
        Assert.Contains("CLIENT_CONFIG_DIR", ex.Message);
    }

    [Fact]
    public async Task Remove_DeletesFile_AndIsNoOpWhenMissingOrDisabled()
    {
        var store = CreateStore();
        await store.WriteAsync("db1", "10.50.28.201", CancellationToken.None);

        store.Remove("db1");
        store.Remove("db1");
        CreateStore(enabled: false).Remove("db1");

        Assert.False(File.Exists(_dir.Combine("ccd", "db1")));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("..")]
    public void Remove_RefusesNamesOutsideCcdDir(string cn)
    {
        Assert.Throws<LocalIssuanceValidationException>(() => CreateStore().Remove(cn));
    }
}
