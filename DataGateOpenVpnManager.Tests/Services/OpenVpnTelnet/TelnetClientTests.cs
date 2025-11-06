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
    private readonly int _testPort = 65000; // неиспользуемый порт

    public TelnetClientTests()
    {
        _loggerMock = new Mock<ILogger<TelnetClient>>();
    }

    [Fact]
    public async Task SendAsync_WhenNotConnectedAndConnectionFails_ThrowsTimeoutException()
    {
        // Arrange
        var client = new TelnetClient(_testHost, _testPort, _loggerMock.Object);
        var command = "test command";
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(() =>
            client.SendAsync(command, cts.Token));

        _loggerMock.Verify(x => x.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Not connected")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()
        ), Times.AtLeastOnce);

        _loggerMock.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("timed out")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()
        ), Times.Once);
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
        ), Times.AtMostOnce); // или Times.Never — если уверен

    }

    [Fact]
    public async Task EnsureConnectedAsync_WhenConnectionFails_ThrowsTimeoutExceptionAndLogsError()
    {
        // Arrange
        var client = new TelnetClient(_testHost, _testPort, _loggerMock.Object);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(() =>
            client.EnsureConnectedAsync(cts.Token));

        _loggerMock.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("timed out")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()
        ), Times.Once);
    }

    [Fact]
    public void OnDataReceived_WhenInvoked_TriggersHandler()
    {
        // Arrange
        var client = new TelnetClient(_testHost, _testPort, _loggerMock.Object);
        var testData = "Test message";
        string? receivedData = null;

        client.OnDataReceived += (data) => receivedData = data;

        // Act — вызов через `Invoke` делегата напрямую
        var fieldInfo = typeof(TelnetClient).GetField("OnDataReceived",
            BindingFlags.Instance | BindingFlags.NonPublic);

        var handler = fieldInfo?.GetValue(client) as Action<string>;
        handler?.Invoke(testData);

        // Assert
        Assert.Equal(testData, receivedData);
    }
}
