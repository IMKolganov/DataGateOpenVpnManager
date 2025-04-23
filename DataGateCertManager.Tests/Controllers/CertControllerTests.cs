using DataGateCertManager.Controllers;
using DataGateCertManager.Models;
using DataGateCertManager.Services.EasyRsaServices.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace DataGateCertManager.Tests.Controllers;

public class CertControllerTests
{
    private readonly Mock<IEasyRsaService> _rsaServiceMock = new();
    private readonly Mock<IConfiguration> _configMock = new();
    private readonly Mock<ILogger<CertController>> _loggerMock = new();

    private CertController CreateController()
    {
        var controller = new CertController(_rsaServiceMock.Object, _configMock.Object, _loggerMock.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };
        return controller;
    }

    [Fact]
    public async Task BuildCertificate_ReturnsOk()
    {
        // Arrange
        var controller = CreateController();
        _configMock.Setup(c => c["EasyRsa:Path"]).Returns("/test/path");

        var expected = new CertificateBuildResult { CertificatePath = "cert.pem" };
        _rsaServiceMock
            .Setup(s => s.AddServerCertificate("/test/path", It.IsAny<CancellationToken>(), "test"))
            .ReturnsAsync(expected);

        // Act
        var result = await controller.AddServerCertificate(new AddServerCertificateRequest { CommonName = "test" });

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(expected, ok.Value);
    }

    [Fact]
    public async Task RevokeCertificate_ReturnsOk()
    {
        // Arrange
        var controller = CreateController();
        _configMock.Setup(c => c["EasyRsa:Path"]).Returns("/test/path");

        var expected = new CertificateRevokeResult
        {
            CertificatePath = "/test/path/client1.crt",
            IsRevoked = true,
            Message = "Revocation successful"
        };

        _rsaServiceMock
            .Setup(s => s.RevokeCertificate("/test/path", "client1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await controller.RevokeCertificate("client1");

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var value = Assert.IsType<CertificateRevokeResult>(ok.Value);
        Assert.True(value.IsRevoked);
        Assert.Equal(expected.CertificatePath, value.CertificatePath);
        Assert.Equal(expected.Message, value.Message);
    }

    [Fact]
    public async Task GetAllCertificates_ReturnsList()
    {
        var controller = CreateController();
        _configMock.Setup(c => c["EasyRsa:PkiPath"]).Returns("/pki");

        var expected = new List<CertificateCaInfo> { new() { CommonName = "test" } };
        _rsaServiceMock
            .Setup(s => s.GetAllCertificateInfoInIndexFile("/pki", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await controller.GetAllCertificates();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(expected, ok.Value);
    }

    [Fact]
    public async Task GetPemContent_ReturnsString()
    {
        var controller = CreateController();

        _rsaServiceMock
            .Setup(s => s.ReadPemContent("client1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("pem content");

        var result = await controller.GetPemContent("client1");

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("pem content", ok.Value);
    }

    [Fact]
    public async Task BuildCertificate_ReturnsBadRequest_WhenExceptionThrown()
    {
        var controller = CreateController();
        _configMock.Setup(c => c["EasyRsa:Path"]).Throws(new Exception("config error"));

        var result = await controller.BuildCertificate(new AddServerCertificateRequest { CommonName = "test" });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("config error", badRequest.Value!.ToString());
    }
}
