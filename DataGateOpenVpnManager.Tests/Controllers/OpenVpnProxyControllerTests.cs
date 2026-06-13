using DataGateOpenVpnManager.Controllers;
using DataGateOpenVpnManager.Services.Proxy;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy.Enums;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy.Requests;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy.Responses;
using DataGateMonitor.SharedModels.Responses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace DataGateOpenVpnManager.Tests.Controllers;

public class OpenVpnProxyControllerTests
{
    private static OpenVpnProxyController CreateController(
        IActiveProxyConnectionService active,
        IProxyConnectionHistoryService? history = null,
        IProxyTrafficFlowService? trafficFlow = null,
        IProxyConnectionIdentityResolver? identityResolver = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var logger = new Mock<ILogger<OpenVpnProxyController>>();
        history ??= new Mock<IProxyConnectionHistoryService>().Object;
        trafficFlow ??= new Mock<IProxyTrafficFlowService>().Object;
        identityResolver ??= new Mock<IProxyConnectionIdentityResolver>().Object;
        return new OpenVpnProxyController(config, logger.Object, active, history, trafficFlow, identityResolver);
    }

    [Fact]
    public void GetClientByLocalPort_ReturnsNotFound_WhenNoConnection()
    {
        var active = new ActiveProxyConnectionService();
        var controller = CreateController(active);

        var result = controller.GetClientByLocalPort(new GetProxyClientByLocalPortRequest
        {
            LocalPort = 65000,
            Host = "localhost"
        });

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        var notFoundBody = Assert.IsType<ApiResponse<ProxyClientLookupResponse>>(notFound.Value);
        Assert.False(notFoundBody.Success);
        Assert.Contains("No active proxy session", notFoundBody.Message);
    }

    [Fact]
    public void GetClientByLocalPort_ReturnsBadRequest_WhenPortInvalid()
    {
        var active = new ActiveProxyConnectionService();
        var controller = CreateController(active);

        var result = controller.GetClientByLocalPort(new GetProxyClientByLocalPortRequest { LocalPort = 0 });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var badRequestBody = Assert.IsType<ApiResponse<ProxyClientLookupResponse>>(badRequest.Value);
        Assert.False(badRequestBody.Success);
    }

    [Fact]
    public void GetClientByLocalPort_ReturnsOk_WithConnection()
    {
        var active = new ActiveProxyConnectionService();
        var expected = new ActiveProxyConnection
        {
            ConnectionId = "conn-1",
            Protocol = ProxyConnectionProtocol.Udp,
            RealClientIp = "192.0.2.10",
            RealClientPort = 48000,
            LocalProxyIp = "127.0.0.1",
            LocalProxyPort = 41234,
            TargetIp = "127.0.0.1",
            TargetPort = 1194,
            ConnectedAtUtc = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc)
        };
        active.Add(expected);

        var controller = CreateController(active);

        var result = controller.GetClientByLocalPort(new GetProxyClientByLocalPortRequest
        {
            LocalPort = 41234,
            Host = "localhost"
        });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var wrapped = Assert.IsType<ApiResponse<ProxyClientLookupResponse>>(ok.Value);
        Assert.True(wrapped.Success);
        Assert.NotNull(wrapped.Data);
        var value = wrapped.Data!;
        Assert.Equal("127.0.0.1", value.Host);
        Assert.Equal("conn-1", value.ConnectionId);
        Assert.Equal(ProxyConnectionProtocol.Udp, value.Protocol);
        Assert.Equal("192.0.2.10", value.RealClientIp);
        Assert.Equal(41234, value.LocalProxyPort);
    }
}
