using DataGateOpenVpnManager.Hubs;
using DataGateOpenVpnManager.Services.Proxy;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy.Enums;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataGateOpenVpnManager.Tests.Services.Proxy;

public class ProxyTrafficFlowBroadcastServiceTests
{
    [Fact]
    public async Task ExecuteAsync_BroadcastsBatch_WhenFlowHasUpdates()
    {
        var flow = new ProxyTrafficFlowService();
        flow.RegisterConnectFailed(
            "failed-1",
            ProxyConnectionProtocol.Tcp,
            "203.0.113.1",
            50000,
            null,
            "127.0.0.1",
            1194,
            "boom");

        var clientProxy = new Mock<IClientProxy>();
        clientProxy.Setup(c => c.SendCoreAsync(
                "TrafficFlowUpdated",
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.All).Returns(clientProxy.Object);
        var hubContext = new Mock<IHubContext<ProxyTrafficFlowHub>>();
        hubContext.Setup(h => h.Clients).Returns(clients.Object);

        var service = new ProxyTrafficFlowBroadcastService(
            flow,
            hubContext.Object,
            NullLogger<ProxyTrafficFlowBroadcastService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(1200);
        await service.StopAsync(CancellationToken.None);

        clientProxy.Verify(
            c => c.SendCoreAsync("TrafficFlowUpdated", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }
}
