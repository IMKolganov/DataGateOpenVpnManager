using DataGateOpenVpnManager.Services.OpenVpnTelnet;

namespace DataGateOpenVpnManager.Tests.Services.OpenVpnTelnet;

public class OpenVpnManagementMessageCompletionTests
{
    [Theory]
    [InlineData("1781288640,CONNECTED,SUCCESS,10.51.30.1,,,,\nEND")]
    [InlineData("1781288640,CONNECTED,SUCCESS,10.51.30.1,,,,\r\nEND\r\n")]
    [InlineData("END")]
    public void IsComplete_TrueForStateResponseEndingWithEndLine(string message)
    {
        Assert.True(OpenVpnManagementMessageCompletion.IsComplete(message));
    }

    [Fact]
    public void IsComplete_FalseWhenHelpTextContainsEndWordBeforeTerminator()
    {
        const string partialHelp =
            "rsa-sig                : Enter a signature in response to >RSA_SIGN challenge\n" +
            "                         Enter signature base64 on subsequent lines followed by END\n" +
            "pk-sig                 : Enter a signature";

        Assert.False(OpenVpnManagementMessageCompletion.IsComplete(partialHelp));
    }

    [Fact]
    public void IsComplete_TrueWhenFullHelpEndsWithEndLine()
    {
        const string fullHelp =
            "rsa-sig                : Enter a signature in response to >RSA_SIGN challenge\n" +
            "                         Enter signature base64 on subsequent lines followed by END\n" +
            "signal s\n" +
            "END";

        Assert.True(OpenVpnManagementMessageCompletion.IsComplete(fullHelp));
    }

    [Theory]
    [InlineData("SUCCESS: test completed")]
    [InlineData("ERROR: test failed")]
    [InlineData("NOTIFY: event occurred")]
    [InlineData("NOTICE: system message")]
    public void IsComplete_TrueForSingleLineStatusPrefixes(string message)
    {
        Assert.True(OpenVpnManagementMessageCompletion.IsComplete(message));
    }

    [Fact]
    public void EndsWithEndLine_FalseForEmbeddedEndWordOnly()
    {
        Assert.False(OpenVpnManagementMessageCompletion.EndsWithEndLine(
            "Enter certificate base64 on subsequent lines followed by END"));
    }
}
