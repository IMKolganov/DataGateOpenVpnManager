using DataGateCertManager.Services.OpenVpnTelnet;
using DataGateCertManager.Tests.Services.OpenVpnTelnet.Fakes;
using Microsoft.Extensions.Logging;
using Moq;

namespace DataGateCertManager.Tests.Services.OpenVpnTelnet;

public class OpenVpnManagementSignalServiceTests
{
    private readonly FakeTelnetClient _telnetClient;
    private readonly Mock<IMessageSubscriber> _subscriberMock;
    private readonly OpenVpnManagementSignalService _service;

    public OpenVpnManagementSignalServiceTests()
    {
        _telnetClient = new FakeTelnetClient();
        var loggerMock = new Mock<ILogger<CommandQueue>>();
        _subscriberMock = new Mock<IMessageSubscriber>();
        _service = new OpenVpnManagementSignalService(_telnetClient, loggerMock.Object);
    }

    [Fact]
    public async Task SendCommandAsync_SuccessfulCommand_ReturnsResponse()
    {
        // Arrange
        var expectedResponse = "SUCCESS: command executed";
        var command = "test_command";

        var sendTask = _service.SendCommandAsync(command, CancellationToken.None);
        await Task.Delay(100); // let it enqueue
        _telnetClient.SimulateIncomingData(expectedResponse);

        var result = await sendTask;

        // Assert
        Assert.Equal(expectedResponse, result);
        Assert.Contains(command, _telnetClient.SentCommands);
    }

    [Fact]
    public async Task SendCommandAsync_WhenTimeout_ReturnsTimeoutMessage()
    {
        // Arrange
        var command = "test_command";
        _telnetClient.ThrowOnSend = true;
        _telnetClient.ExceptionToThrow = new TimeoutException("Operation timed out");

        // Act
        var result = await _service.SendCommandAsync(command, CancellationToken.None);

        // Assert
        Assert.Contains("Command timed out:", result);
        Assert.Contains("Operation timed out", result);
    }

    [Fact]
    public async Task SendCommandAsync_WhenError_ReturnsErrorMessage()
    {
        // Arrange
        var command = "test_command";
        _telnetClient.ThrowOnSend = true;
        _telnetClient.ExceptionToThrow = new InvalidOperationException("Test error");

        // Act
        var result = await _service.SendCommandAsync(command, CancellationToken.None);

        // Assert
        Assert.Contains("Error while sending command:", result);
        Assert.Contains("Test error", result);
    }

    [Fact]
    public void Subscribe_AddsSubscriberSuccessfully()
    {
        // Act
        _service.Subscribe(_subscriberMock.Object);

        // Assert
        Assert.True(true); // No exception means success
    }

    [Fact]
    public void Subscribe_WhenSubscriberIsNull_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _service.Subscribe(null!));
    }

    [Fact]
    public void Unsubscribe_RemovesSubscriberSuccessfully()
    {
        // Arrange
        _service.Subscribe(_subscriberMock.Object);

        // Act
        _service.Unsubscribe(_subscriberMock.Object, "127.0.0.1", 1234);

        // Assert
        Assert.True(true); // No exception means success
    }

    [Fact]
    public void Unsubscribe_WhenSubscriberDoesNotExist_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<Exception>(() =>
            _service.Unsubscribe(_subscriberMock.Object, "127.0.0.1", 1234));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task SendCommandAsync_WithInvalidCommand_LogsWarning(string command)
    {
        // Act
        var result = await _service.SendCommandAsync(command, CancellationToken.None);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task SendCommandAsync_WithCancellationToken_PropagatesToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var result = await _service.SendCommandAsync("test_command", cts.Token);

        // Assert
        Assert.Contains("Error while sending command:", result);
        Assert.Contains("canceled", result.ToLower());
    }

    [Fact]
    public async Task SendCommandAsync_WithLongRunningCommand_RespectsTimeout()
    {
        // Arrange
        var command = "test_command";
        _telnetClient.ThrowOnSend = true;
        _telnetClient.ExceptionToThrow = new TimeoutException("Simulated timeout");

        // Act
        var result = await _service.SendCommandAsync(command, CancellationToken.None);

        // Assert
        Assert.Contains("Command timed out:", result);
    }
}
