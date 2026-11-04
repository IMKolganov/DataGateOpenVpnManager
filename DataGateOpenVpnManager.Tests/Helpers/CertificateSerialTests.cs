using DataGateOpenVpnManager.Helpers;

namespace DataGateOpenVpnManager.Tests.Helpers;

public class CertificateSerialTests
{
    [Theory]
    [InlineData("F5A1C0FFEE0011223344556677889900", "00F5A1C0FFEE0011223344556677889900")]
    [InlineData("0CDE25A6AF8C33195EE2DBC6A53BD5", "CDE25A6AF8C33195EE2DBC6A53BD5")]
    [InlineData("4bfb5d62", "4B:FB:5D:62")]
    public void AreEqual_IgnoresSignByteLeadingZerosCaseAndColons(string left, string right) =>
        Assert.True(CertificateSerial.AreEqual(left, right));

    [Theory]
    [InlineData("5E71A1", "5E71A2")]
    [InlineData("5E71A1", "5E71A100")]
    [InlineData("5E71A1", null)]
    [InlineData("", "")]
    public void AreEqual_DifferentOrMissing_IsFalse(string? left, string? right) =>
        Assert.False(CertificateSerial.AreEqual(left, right));

    [Fact]
    public void TryReadFromFile_MissingFile_IsNull() =>
        Assert.Null(CertificateSerial.TryReadFromFile(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "x.crt")));
}
