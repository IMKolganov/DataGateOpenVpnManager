using DataGateOpenVpnManager.Services.OpenVpnTelnet;
using Microsoft.Extensions.Logging;
using Moq;

namespace DataGateOpenVpnManager.Tests.Services;

public class OpenVpnManagementSignalServiceTests
{
    private sealed class TestSubscriber : IMessageSubscriber
    {
        public Task OnMessageReceived(string message, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static OpenVpnManagementSignalService CreateService()
    {
        var telnet = new TelnetClient("127.0.0.1", 65000, new Microsoft.Extensions.Logging.Abstractions.NullLogger<TelnetClient>());
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<CommandQueue>();
        return new OpenVpnManagementSignalService(telnet, logger);
    }

    [Fact]
    public void Subscribe_WhenSubscriberNull_ThrowsArgumentNullException()
    {
        var service = CreateService();
        Assert.Throws<ArgumentNullException>(() => service.Subscribe(null!));
    }

    [Fact]
    public void Unsubscribe_WhenSubscriberNull_ThrowsArgumentNullException()
    {
        var service = CreateService();
        Assert.Throws<ArgumentNullException>(() => service.Unsubscribe(null!, "127.0.0.1", 0));
    }

    [Fact]
    public async Task SendCommandAsync_WhenCommandNull_ReturnsEmptyString()
    {
        var service = CreateService();
        var result = await service.SendCommandAsync(null!, CancellationToken.None);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task SendCommandAsync_WhenCommandEmpty_ReturnsEmptyString()
    {
        var service = CreateService();
        var result = await service.SendCommandAsync("   ", CancellationToken.None);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Subscribe_WhenSubscriberValid_DoesNotThrow()
    {
        var service = CreateService();
        service.Subscribe(new TestSubscriber());
    }

    [Fact]
    public void Unsubscribe_WhenSubscriberValid_DoesNotThrow()
    {
        var service = CreateService();
        var sub = new TestSubscriber();
        service.Subscribe(sub);
        service.Unsubscribe(sub, "127.0.0.1", 0);
    }
}
