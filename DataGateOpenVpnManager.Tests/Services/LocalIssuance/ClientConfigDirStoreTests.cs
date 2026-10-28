using DataGateOpenVpnManager.Services.LocalIssuance;
using DataGateOpenVpnManager.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace DataGateOpenVpnManager.Tests.Services.LocalIssuance;

public class ClientConfigDirStoreTests : IDisposable
{
    private readonly TempDir _dir = new();

    private ClientConfigDirStore CreateStore(bool enabled = true, bool exclusive = false) =>
        new(new LocalIssuanceOptions
        {
            ClientConfigDir = enabled ? _dir.Combine("ccd") : null,
            CcdExclusive = exclusive,
            VpnSubnet = "10.50.28.0",
            VpnNetmask = "255.255.255.0",
            IppFile = _dir.Combine("ipp.txt"),
        }, NullLogger<ClientConfigDirStore>.Instance);

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
    public void ValidateStaticIp_RejectsUnusableAddresses(string ip, string expected)
    {
        var ex = Assert.Throws<LocalIssuanceValidationException>(() => CreateStore().ValidateStaticIp("db1", ip));
        Assert.Contains(expected, ex.Message);
        Assert.False(ex.IsConflict);
    }

    [Fact]
    public async Task ValidateStaticIp_RejectsAddressPinnedToAnotherCn()
    {
        var store = CreateStore();
        await store.WriteAsync("db1", "10.50.28.201", CancellationToken.None);

        var ex = Assert.Throws<LocalIssuanceValidationException>(() => store.ValidateStaticIp("db2", "10.50.28.201"));

        Assert.True(ex.IsConflict);
        Assert.Contains("db1", ex.Message);
        store.ValidateStaticIp("db1", "10.50.28.201"); // re-issuing the same CN keeps its address
    }

    [Fact]
    public void ValidateStaticIp_RejectsAddressHeldInDynamicPool()
    {
        File.WriteAllText(_dir.Combine("ipp.txt"), "laptop,10.50.28.7,\n");

        var ex = Assert.Throws<LocalIssuanceValidationException>(() => CreateStore().ValidateStaticIp("db1", "10.50.28.7"));

        Assert.True(ex.IsConflict);
        Assert.Contains("laptop", ex.Message);
    }

    [Fact]
    public void ValidateStaticIp_WhenCcdDisabled_ExplainsHowToEnable()
    {
        var ex = Assert.Throws<LocalIssuanceValidationException>(() => CreateStore(enabled: false).ValidateStaticIp("db1", "10.50.28.201"));
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
