using DataGateOpenVpnManager.Helpers;

namespace DataGateOpenVpnManager.Tests.Helpers;

public class CommonNameValidatorTests
{
    [Theory]
    [InlineData("upcloud-db1")]
    [InlineData("svc.tang_gate-01")]
    [InlineData("A")]
    public void TryValidate_AcceptsSafeNames(string cn)
    {
        Assert.True(CommonNameValidator.TryValidate(cn, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("-leading-dash")]
    [InlineData(".hidden")]
    [InlineData("a/b")]
    [InlineData("a..b")]
    [InlineData("with space")]
    [InlineData("semi;colon")]
    [InlineData("new\nline")]
    [InlineData("server")]
    [InlineData("CA")]
    public void TryValidate_RejectsUnsafeOrReservedNames(string cn)
    {
        Assert.False(CommonNameValidator.TryValidate(cn, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void TryValidate_RejectsLongerThan64()
    {
        Assert.True(CommonNameValidator.TryValidate(new string('a', 64), out _));
        Assert.False(CommonNameValidator.TryValidate(new string('a', 65), out _));
    }
}
