using DataGateOpenVpnManager.Hubs;
using DataGateOpenVpnManager.Services.OpenVpnEvent.Subscribers;
using DataGateOpenVpnManager.Services.OpenVpnTelnet.Subscribers;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace DataGateOpenVpnManager.Tests.Services.OpenVpnTelnet;

public class MessageSubscriberTests
{
    [Fact]
    public async Task SignalRMessageSubscriber_ForwardsMessageToCaller()
    {
        var clientProxy = new Mock<ISingleClientProxy>();
        clientProxy.Setup(c => c.SendCoreAsync("ReceiveMessage", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Client("conn-1")).Returns(clientProxy.Object);
        var hubContext = new Mock<IHubContext<OpenVpnSignalHub>>();
        hubContext.Setup(h => h.Clients).Returns(clients.Object);

        var subscriber = new SignalRMessageSubscriber(hubContext.Object, "conn-1");
        await subscriber.OnMessageReceived("CLIENT_LIST\nEND", CancellationToken.None);

        clientProxy.Verify(c => c.SendCoreAsync("ReceiveMessage", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OpenVpnEventsMessageSubscriber_ForwardsMessageToCaller()
    {
        var clientProxy = new Mock<ISingleClientProxy>();
        clientProxy.Setup(c => c.SendCoreAsync("ReceiveEventMessage", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Client("conn-2")).Returns(clientProxy.Object);
        var hubContext = new Mock<IHubContext<OpenVpnEventHub>>();
        hubContext.Setup(h => h.Clients).Returns(clients.Object);

        var subscriber = new OpenVpnEventsMessageSubscriber(hubContext.Object, "conn-2");
        await subscriber.OnMessageReceived("event payload", CancellationToken.None);

        clientProxy.Verify(c => c.SendCoreAsync("ReceiveEventMessage", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
