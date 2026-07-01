using DataGateOpenVpnManager.Services.PiHole;

namespace DataGateOpenVpnManager.Tests.Services.PiHole;

public class PiHoleSubnetFilterTests
{
    [Theory]
    [InlineData("10.51.30.2", "10.51.30.", true)]
    [InlineData("10.51.31.2", "10.51.30.", false)]
    [InlineData("10.51.30.2", null, true)]
    [InlineData("10.51.30.2", "", true)]
    public void Matches_RespectsPrefix(string clientIp, string? prefix, bool expected) =>
        Assert.Equal(expected, PiHoleSubnetFilter.Matches(clientIp, prefix));

    [Fact]
    public void Apply_FiltersRecordsBySubnet()
    {
        var records = new[]
        {
            new PiHoleQueryRecord(1, "10.51.30.1", "a.example", null, "FORWARDED", DateTimeOffset.UtcNow),
            new PiHoleQueryRecord(2, "10.51.31.1", "b.example", null, "FORWARDED", DateTimeOffset.UtcNow),
        };

        var filtered = PiHoleSubnetFilter.Apply(records, "10.51.30.");

        Assert.Single(filtered);
        Assert.Equal("a.example", filtered[0].Domain);
    }
}
