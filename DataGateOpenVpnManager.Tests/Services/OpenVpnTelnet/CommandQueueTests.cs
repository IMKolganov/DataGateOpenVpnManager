using DataGateOpenVpnManager.Services.OpenVpnTelnet;
using DataGateOpenVpnManager.Tests.Services.OpenVpnTelnet.Fakes;
using Microsoft.Extensions.Logging;
using Moq;

namespace DataGateOpenVpnManager.Tests.Services.OpenVpnTelnet;

public class CommandQueueTests
{
    private readonly FakeTelnetClient _telnetClient;
    private readonly Mock<IMessageSubscriber> _subscriberMock;
    private readonly CommandQueue _queue;

    public CommandQueueTests()
    {
        var loggerMock = new Mock<ILogger<CommandQueue>>();
        _subscriberMock = new Mock<IMessageSubscriber>();
        _telnetClient = new FakeTelnetClient();
        _queue = new CommandQueue(_telnetClient, loggerMock.Object);
    }

    private void SimulateDataReceived(string message)
    {
        _telnetClient.SimulateIncomingData(message);
    }

    [Fact]
    public void Subscribe_AddsSubscriberToList()
    {
        // Act
        _queue.Subscribe(_subscriberMock.Object);

        // Assert
        Assert.True(_queue.HasSubscribers);
    }

    [Fact]
    public void Unsubscribe_RemovesSubscriberFromList()
    {
        // Arrange
        _queue.Subscribe(_subscriberMock.Object);

        // Act
        _queue.Unsubscribe(_subscriberMock.Object, "127.0.0.1", 1234);

        // Assert
        Assert.False(_queue.HasSubscribers);
    }

    [Fact]
    public void Unsubscribe_WhenSubscriberDoesNotExist_ThrowsException()
    {
        // Act & Assert
        var ex = Assert.Throws<Exception>(() => 
            _queue.Unsubscribe(_subscriberMock.Object, "127.0.0.1", 1234));
        Assert.Equal("Subscriber doesn't exist", ex.Message);
    }

    [Theory]
    [InlineData("SUCCESS: test completed")]
    [InlineData("ERROR: test failed")]
    [InlineData("NOTIFY: event occurred")]
    [InlineData("NOTICE: system message")]
    [InlineData("Command output\nEND")]
    public async Task SendCommandAsync_WithValidResponse_ReturnsResponse(string response)
    {
        // Arrange
        var command = "test command";
        var cts = new CancellationTokenSource();
        _telnetClient.Reset();

        // Act
        var sendTask = _queue.SendCommandAsync(command, cts.Token);
        await Task.Delay(50); // Give time for the command to register
        SimulateDataReceived(response);
        var result = await sendTask;

        // Assert
        Assert.Equal(response, result);
        Assert.Single(_telnetClient.SentCommands);
        Assert.Equal(command, _telnetClient.SentCommands[0]);
    }

    [Fact]
    public async Task SendCommandAsync_WhenTimeout_ThrowsTimeoutException()
    {
        // Arrange
        var command = "test command";
        var timeoutMs = 100;
        var cts = new CancellationTokenSource();

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await _queue.SendCommandAsync(command, cts.Token, timeoutMs));
    }

    [Fact]
    public async Task SendCommandAsync_WhenDisconnected_ThrowsInvalidOperationException()
    {
        // Arrange
        await _queue.DisconnectAsync();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _queue.SendCommandAsync("test", CancellationToken.None));
    }

    [Fact]
    public void HandleIncomingMessage_WithSubscriber_NotifiesSubscriber()
    {
        // Arrange
        _queue.Subscribe(_subscriberMock.Object);
        var message = "test message";

        // Act
        SimulateDataReceived(message);

        // Assert
        _subscriberMock.Verify(x => x.OnMessageReceived(
            message, 
            It.IsAny<CancellationToken>()), 
            Times.Once);
    }

    [Fact]
    public void TryGetMessage_WhenMessageExists_ReturnsMessage()
    {
        // Arrange
        var message = "test message";
        SimulateDataReceived(message);

        // Act
        var (success, result) = _queue.TryGetMessage();

        // Assert
        Assert.True(success);
        Assert.Equal(message, result);
    }

    [Fact]
    public async Task IsAliveAsync_WhenResponseReceived_ReturnsTrue()
    {
        // Arrange
        var cts = new CancellationTokenSource();

        // Act
        var checkTask = _queue.IsAliveAsync(cts.Token);
        await Task.Delay(100); // Give time for the command to be processed
        SimulateDataReceived("SUCCESS: echo");
        var result = await checkTask;

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task DisconnectAsync_WithNoSubscribers_DisconnectsTelnetClient()
    {
        // Arrange
        _telnetClient.Reset();

        // Act
        await _queue.DisconnectAsync();

        // Assert
        Assert.Equal(1, _telnetClient.DisconnectCallCount);
    }

    [Fact]
    public async Task DisconnectAsync_WithSubscribers_DoesNotDisconnectTelnetClient()
    {
        // Arrange
        _telnetClient.Reset();
        _queue.Subscribe(_subscriberMock.Object);

        // Act
        await _queue.DisconnectAsync();

        // Assert
        Assert.Equal(0, _telnetClient.DisconnectCallCount);
    }

    [Fact]
    public void HandleIncomingMessage_WithNullOrWhitespace_DoesNotProcess()
    {
        // Arrange
        _queue.Subscribe(_subscriberMock.Object);

        // Act
        SimulateDataReceived("");
        SimulateDataReceived(null!);
        SimulateDataReceived("   ");

        // Assert
        _subscriberMock.Verify(x => x.OnMessageReceived(
            It.IsAny<string>(), 
            It.IsAny<CancellationToken>()), 
            Times.Never);
    }

    [Fact]
    public async Task DisposeAsync_CallsDisconnectAsync()
    {
        // Arrange
        _telnetClient.Reset();

        // Act
        await _queue.DisposeAsync();

        // Assert
        Assert.Equal(1, _telnetClient.DisconnectCallCount);
    }


    [Fact]
    public async Task SendCommandAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _queue.SendCommandAsync("test", cts.Token));
    }

    [Fact]
    public async Task SendCommandAsync_WhenTelnetClientThrows_PropagatesException()
    {
        // Arrange
        _telnetClient.ThrowOnSend = true;
        _telnetClient.ExceptionToThrow = new IOException("Connection failed");

        // Act & Assert
        await Assert.ThrowsAsync<IOException>(async () =>
            await _queue.SendCommandAsync("test", CancellationToken.None));
    }
}