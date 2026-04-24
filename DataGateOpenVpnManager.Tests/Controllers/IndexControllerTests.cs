using DataGateOpenVpnManager.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Info;

namespace DataGateOpenVpnManager.Tests.Controllers;

public class IndexControllerTests
{
    private readonly Mock<IWebHostEnvironment> _envMock;
    private readonly Mock<ILogger<IndexController>> _loggerMock;

    public IndexControllerTests()
    {
        _envMock = new Mock<IWebHostEnvironment>();
        _envMock.Setup(e => e.EnvironmentName).Returns("Testing");
        _loggerMock = new Mock<ILogger<IndexController>>();
    }

    [Fact]
    public async Task Get_ReturnsOk_WithRootOpenVpnInfoResponse()
    {
        var configData = new Dictionary<string, string?>
        {
            ["DNS1"] = "8.8.8.8",
            ["DNS2"] = "8.8.4.4",
            ["VPN_SUBNET"] = "10.51.28.0",
            ["VPN_NETMASK"] = "255.255.255.0",
            ["EASY_RSA_PATH"] = "/path/easy-rsa",
            ["DATA_DIR"] = "/data",
            ["PORT"] = "1194",
            ["API_PORT"] = "5010",
            ["PROTO"] = "udp",
            ["OpenVpnManagement:Host"] = "127.0.0.1",
            ["OpenVpnManagement:Port"] = "5092",
            ["BACKEND__BASEURL"] = "http://backend/"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configData!).Build();

        var controller = new IndexController(config, _envMock.Object, _loggerMock.Object);

        var result = await controller.Get(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<RootOpenVpnInfoResponse>(okResult.Value);
        Assert.Equal("DataGateOpenVpnManager", response.Application);
        Assert.Equal("Testing", response.Environment);
        Assert.NotNull(response.Version);
        Assert.NotNull(response.Config);
        Assert.Equal("8.8.8.8", response.Config.Dns1);
        Assert.Equal("1194", response.Config.Port);
        Assert.Equal("5092", response.Config.OpenVpnManagement?.Port);
    }

    [Fact]
    public async Task Get_WhenConfigMissingKeys_StillReturnsOk_WithNullOrEmptyConfigValues()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var controller = new IndexController(config, _envMock.Object, _loggerMock.Object);

        var result = await controller.Get(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<RootOpenVpnInfoResponse>(okResult.Value);
        Assert.NotNull(response.Config);
    }
}
