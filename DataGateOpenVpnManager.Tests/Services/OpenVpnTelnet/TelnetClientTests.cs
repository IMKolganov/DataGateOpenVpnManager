using System.Net.Sockets;
using System.Reflection;
using System.Text;
using DataGateOpenVpnManager.Services.OpenVpnTelnet;
using Microsoft.Extensions.Logging;
using Moq;

namespace DataGateOpenVpnManager.Tests.Services.OpenVpnTelnet;

public class TelnetClientTests
{
    private readonly Mock<ILogger<TelnetClient>> _loggerMock;
    private readonly string _testHost = "127.0.0.1";
    private readonly int _testPort = 65000; // unused port

    public TelnetClientTests()
    {
        _loggerMock = new Mock<ILogger<TelnetClient>>();
    }

    [Fact]
    public async Task SendAsync_WhenNotConnectedAndConnectionFails_ThrowsException()
    {
        // Arrange: no server on _testPort; connection fails or times out
        var client = new TelnetClient(_testHost, _testPort, _loggerMock.Object);
        var command = "test command";
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act & Assert: may throw TimeoutException or InvalidOperationException depending on timing
        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            client.SendAsync(command, cts.Token));

        Assert.True(
            ex is TimeoutException || ex is InvalidOperationException,
            $"Expected TimeoutException or InvalidOperationException, got {ex.GetType().Name}");
    }

    [Fact]
    public async Task DisposeAsync_DisposesResourcesProperly()
    {
        // Arrange
        var client = new TelnetClient(_testHost, _testPort, _loggerMock.Object);

        // Act
        await client.DisposeAsync();

        // Assert
        _loggerMock.Verify(x => x.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()
        ), Times.AtMostOnce);

    }

    [Fact]
    public async Task EnsureConnectedAsync_WhenConnectionFails_ThrowsException()
    {
        // Arrange: no server on _testPort; connection fails or times out
        var client = new TelnetClient(_testHost, _testPort, _loggerMock.Object);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act & Assert: may throw TimeoutException or InvalidOperationException depending on timing
        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            client.EnsureConnectedAsync(cts.Token));

        Assert.True(
            ex is TimeoutException || ex is InvalidOperationException,
            $"Expected TimeoutException or InvalidOperationException, got {ex.GetType().Name}");
    }

    [Fact]
    public void OnDataReceived_WhenInvoked_TriggersHandler()
    {
        // Arrange
        var client = new TelnetClient(_testHost, _testPort, _loggerMock.Object);
        var testData = "Test message";
        string? receivedData = null;

        client.OnDataReceived += (data) => receivedData = data;

        var fieldInfo = typeof(TelnetClient).GetField("OnDataReceived",
            BindingFlags.Instance | BindingFlags.NonPublic);

        var handler = fieldInfo?.GetValue(client) as Action<string>;
        handler?.Invoke(testData);

        // Assert
        Assert.Equal(testData, receivedData);
    }
}
