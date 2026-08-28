using System.Net;
using System.Net.Sockets;
using DataGateOpenVpnManager.Controllers;
using DataGateOpenVpnManager.Services.Proxy;
using Microsoft.Extensions.Logging.Abstractions;

namespace DataGateOpenVpnManager.Tests.Controllers;

public class OpenVpnProxyUdpConfigTests
{
    [Fact]
    public void WireConstants_AlignWithBatchPoolAndRustProxy()
    {
        Assert.Equal(UdpWsFraming.MaxFrameBytes, ProxyBatchBufferPool.BufferSize);
        Assert.Equal(UdpWsFraming.BatchCapacityBytes, ProxyBatchBufferPool.BufferSize);
        Assert.Equal(4, OpenVpnProxyController.UdpSendQueueDepth);
        Assert.Equal(4 * 1024 * 1024, OpenVpnProxyController.UdpSocketBufferBytes);
    }

    [Fact]
    public void CreateVpnUdpSocket_BindsLoopbackEphemeral_AndConnectsTarget()
    {
        using var target = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var targetEp = (IPEndPoint)target.Client.LocalEndPoint!;

        using var socket = OpenVpnProxyController.CreateVpnUdpSocket(targetEp, NullLogger.Instance);
        var localEp = Assert.IsType<IPEndPoint>(socket.LocalEndPoint);

        Assert.Equal(AddressFamily.InterNetwork, socket.AddressFamily);
        Assert.Equal(SocketType.Dgram, socket.SocketType);
        Assert.Equal(ProtocolType.Udp, socket.ProtocolType);
        Assert.Equal(IPAddress.Loopback, localEp.Address);
        Assert.True(localEp.Port > 0);
        Assert.True(socket.ReceiveBufferSize > 0);
        Assert.True(socket.SendBufferSize > 0);
    }
}
