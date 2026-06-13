using DataGateOpenVpnManager.Controllers;
using DataGateOpenVpnManager.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.VpnEvent.Requests;

namespace DataGateOpenVpnManager.Tests.Controllers;

public class VpnEventControllerTests
{
    private readonly Mock<ILogger<VpnEventController>> _loggerMock = new();

    private static IHubContext<OpenVpnEventHub> CreateHubContextMock(IClientProxy clientProxy)
    {
        var hubClientsMock = new Mock<IHubClients>();
        hubClientsMock.Setup(c => c.All).Returns(clientProxy);
        var hubContextMock = new Mock<IHubContext<OpenVpnEventHub>>();
        hubContextMock.Setup(h => h.Clients).Returns(hubClientsMock.Object);
        return hubContextMock.Object;
    }

    [Fact]
    public async Task OnClientConnect_ReturnsOk()
    {
        var clientProxyMock = new Mock<IClientProxy>();
        clientProxyMock.Setup(c => c.SendCoreAsync("ClientConnected", It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var hubContext = CreateHubContextMock(clientProxyMock.Object);

        var controller = new VpnEventController(_loggerMock.Object, hubContext);
        var data = new VpnEventRequest { CommonName = "client1", RealAddress = "10.0.0.1" };

        var result = await controller.OnClientConnect(data, CancellationToken.None);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task OnClientDisconnect_ReturnsOk()
    {
        var clientProxyMock = new Mock<IClientProxy>();
        clientProxyMock.Setup(c => c.SendCoreAsync("ClientDisconnected", It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var hubContext = CreateHubContextMock(clientProxyMock.Object);

        var controller = new VpnEventController(_loggerMock.Object, hubContext);
        var data = new VpnEventRequest { CommonName = "client1", DurationSec = 120 };

        var result = await controller.OnClientDisconnect(data, CancellationToken.None);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task OnError_ReturnsOk_AndSendsErrorEvent()
    {
        var clientProxyMock = new Mock<IClientProxy>();
        clientProxyMock.Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var hubContext = CreateHubContextMock(clientProxyMock.Object);

        var controller = new VpnEventController(_loggerMock.Object, hubContext);
        var data = new VpnEventRequest { CommonName = "client1", Message = "Auth failed", EventType = "AuthFailed" };

        var result = await controller.OnError(data, CancellationToken.None);

        Assert.IsType<OkResult>(result);
        clientProxyMock.Verify(c => c.SendCoreAsync("ErrorEvent", It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
