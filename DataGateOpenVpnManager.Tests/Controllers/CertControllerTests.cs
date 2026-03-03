using DataGateOpenVpnManager.Controllers;
using DataGateOpenVpnManager.Helpers;
using DataGateOpenVpnManager.Services.EasyRsaServices.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using OpenVPNGateMonitor.SharedModels.DataGateOpenVpnManager.Cert.Requests;
using OpenVPNGateMonitor.SharedModels.DataGateOpenVpnManager.Cert.Responses;

namespace DataGateOpenVpnManager.Tests.Controllers;

public class CertControllerTests
{
    private readonly Mock<IEasyRsaService> _easyRsaMock = new();
    private readonly Mock<IEasyRsaPathResolver> _pathResolverMock = new();
    private readonly Mock<ILogger<CertController>> _loggerMock = new();

    [Fact]
    public async Task GetAllCertificates_WhenPathAndServiceOk_ReturnsOkWithList()
    {
        var certs = new List<ServerCertificate>
        {
            new() { CommonName = "client1", SerialNumber = "01" }
        };
        _pathResolverMock.Setup(p => p.GetEasyRsaPath()).Returns("/easy-rsa");
        _easyRsaMock
            .Setup(s => s.GetAllCertificateInfoInIndexFileAsync("/easy-rsa", It.IsAny<CancellationToken>()))
            .ReturnsAsync(certs);

        var controller = new CertController(_easyRsaMock.Object, _pathResolverMock.Object, _loggerMock.Object);

        var result = await controller.GetAllCertificates(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<List<ServerCertificate>>(okResult.Value);
        Assert.Single(list);
        Assert.Equal("client1", list[0].CommonName);
    }

    [Fact]
    public async Task GetAllCertificates_WhenServiceThrows_ReturnsBadRequest()
    {
        _pathResolverMock.Setup(p => p.GetEasyRsaPath()).Returns("/easy-rsa");
        _easyRsaMock
            .Setup(s => s.GetAllCertificateInfoInIndexFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException("index.txt not found"));

        var controller = new CertController(_easyRsaMock.Object, _pathResolverMock.Object, _loggerMock.Object);

        var result = await controller.GetAllCertificates(CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.NotNull(badRequest.Value);
    }

    [Fact]
    public async Task AddServerCertificate_WhenRequestValid_ReturnsOkWithCertificate()
    {
        var request = new AddServerCertificateRequest
        {
            CommonName = "newclient",
            CertExpireDays = 365
        };
        var certResult = new ServerCertificate { CommonName = "newclient", CertificatePath = "/pki/issued/newclient.crt", KeyPath = "/pki/private/newclient.key" };
        _pathResolverMock.Setup(p => p.GetEasyRsaPath()).Returns("/easy-rsa");
        _easyRsaMock
            .Setup(s => s.BuildCertificateAsync("/easy-rsa", It.IsAny<CancellationToken>(), "newclient", 365))
            .ReturnsAsync(certResult);

        var controller = new CertController(_easyRsaMock.Object, _pathResolverMock.Object, _loggerMock.Object);

        var result = await controller.AddServerCertificate(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var cert = Assert.IsType<ServerCertificate>(okResult.Value);
        Assert.Equal("newclient", cert.CommonName);
    }

    [Fact]
    public async Task AddServerCertificate_WhenCertExpireDaysZero_SetsTo365()
    {
        var request = new AddServerCertificateRequest { CommonName = "c", CertExpireDays = 0 };
        var certResult = new ServerCertificate { CommonName = "c" };
        _pathResolverMock.Setup(p => p.GetEasyRsaPath()).Returns("/easy-rsa");
        _easyRsaMock
            .Setup(s => s.BuildCertificateAsync("/easy-rsa", It.IsAny<CancellationToken>(), "c", 365))
            .ReturnsAsync(certResult);

        var controller = new CertController(_easyRsaMock.Object, _pathResolverMock.Object, _loggerMock.Object);

        await controller.AddServerCertificate(request, CancellationToken.None);

        _easyRsaMock.Verify(s => s.BuildCertificateAsync("/easy-rsa", It.IsAny<CancellationToken>(), "c", 365), Times.Once);
    }

    [Fact]
    public async Task RevokeCertificate_WhenRequestValid_ReturnsOk()
    {
        var request = new RevokeServerCertificateRequest { CommonName = "oldclient" };
        var revokeResult = new ServerCertificate { CommonName = "oldclient", CertificatePath = "/revoked/oldclient.crt" };
        _pathResolverMock.Setup(p => p.GetEasyRsaPath()).Returns("/easy-rsa");
        _easyRsaMock
            .Setup(s => s.RevokeCertificateAsync("/easy-rsa", "oldclient", It.IsAny<CancellationToken>()))
            .ReturnsAsync(revokeResult);

        var controller = new CertController(_easyRsaMock.Object, _pathResolverMock.Object, _loggerMock.Object);

        var result = await controller.RevokeCertificate(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(revokeResult, okResult.Value);
    }
}
