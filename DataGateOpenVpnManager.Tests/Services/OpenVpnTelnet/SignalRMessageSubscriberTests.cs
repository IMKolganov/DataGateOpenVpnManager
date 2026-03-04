using DataGateOpenVpnManager.Services.OpenVpnTelnet;
using DataGateOpenVpnManager.Tests.Services.OpenVpnTelnet.Fakes;
using Microsoft.Extensions.Logging;
using Moq;

namespace DataGateOpenVpnManager.Tests.Services.OpenVpnTelnet;

public class SignalRMessageSubscriberTests
{
    [Fact]
    public async Task SendCommandAsync_SuccessfulCommand_ReturnsResponse()
    {
        // Arrange
        var telnetClient = new FakeTelnetClient();
        var logger = new Mock<ILogger<CommandQueue>>();
        var service = new OpenVpnManagementSignalService(telnetClient, logger.Object);
        var command = "test_command";
        var expectedResponse = "SUCCESS: command executed";

        var sendTask = service.SendCommandAsync(command, CancellationToken.None);

        await Task.Delay(50); // give time for async queue
        telnetClient.SimulateIncomingData(expectedResponse);

        var result = await sendTask;

        // Assert
        Assert.Equal(expectedResponse, result);
        Assert.Contains(command, telnetClient.SentCommands);
    }

}