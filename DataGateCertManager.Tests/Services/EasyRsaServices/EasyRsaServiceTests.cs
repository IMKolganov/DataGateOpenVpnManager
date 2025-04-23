using System.Reflection;
using DataGateCertManager.Models;
using DataGateCertManager.Models.Dto;
using DataGateCertManager.Models.Enums;
using DataGateCertManager.Services.EasyRsaServices;
using DataGateCertManager.Services.EasyRsaServices.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;

namespace DataGateCertManager.Tests.Services.EasyRsaServices;

public class EasyRsaServiceTests
{
    private readonly Mock<ILogger<IEasyRsaService>> _loggerMock = new();
    private readonly Mock<IEasyRsaParseDbService> _parserMock = new();
    private readonly Mock<IEasyRsaExecCommandService> _execMock = new();

    private readonly EasyRsaService _service;

    public EasyRsaServiceTests()
    {
        _service = new EasyRsaService(_loggerMock.Object, _parserMock.Object, _execMock.Object);
    }

    [Fact]
    public async Task BuildCertificate_ReturnsExpectedResult_WhenSuccess()
    {
        // Arrange
        var baseName = "client1";
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var pkiPath = Path.Combine(tempDir, "pki");

        Directory.CreateDirectory(Path.Combine(pkiPath, "reqs"));
        Directory.CreateDirectory(Path.Combine(pkiPath, "issued"));
        Directory.CreateDirectory(Path.Combine(pkiPath, "private"));
        Directory.CreateDirectory(Path.Combine(pkiPath, "certs_by_serial"));

        var reqPath = Path.Combine(pkiPath, "reqs", $"{baseName}.req");
        var issuedPath = Path.Combine(pkiPath, "issued", $"{baseName}.crt");
        var keyPath = Path.Combine(pkiPath, "private", $"{baseName}.key");
        var pemPath = Path.Combine(pkiPath, "certs_by_serial", "ABC123.pem");

        await File.WriteAllTextAsync(reqPath, "dummy");
        await File.WriteAllTextAsync(issuedPath, "dummy");
        await File.WriteAllTextAsync(keyPath, "dummy");
        await File.WriteAllTextAsync(pemPath, "dummy");

        var expectedCommand = $"cd {Path.GetFullPath(tempDir).Replace('\\', '/')} && ./easyrsa --batch build-client-full {baseName} nopass";

        _execMock
            .Setup(x => x.RunCommand(expectedCommand, It.IsAny<CancellationToken>()))
            .ReturnsAsync(("", "", 0));

        _parserMock
            .Setup(x => x.ParseCertificateInfoInIndexFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new()
                {
                    CommonName = baseName,
                    Status = CertificateStatus.Active,
                    SerialNumber = "ABC123"
                }
            ]);

        _execMock
            .Setup(x => x.RunCommand(It.Is<string>(s => s.Contains("openssl")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("serial=ABC123", "", 0));

        // Act
        var result = await _service.BuildCertificate(tempDir, CancellationToken.None);

        // Assert
        Assert.Equal(Path.GetFullPath(issuedPath), Path.GetFullPath(result.CertificatePath), ignoreCase: true);
        Assert.Equal("ABC123", result.SerialNumber);

        // Cleanup
        Directory.Delete(tempDir, recursive: true);
    }
    
    [Fact]
    public async Task ReadPemContent_ReturnsCorrectBlock()
    {
        // Arrange
        var pemLines = new[]
        {
            "Random text",
            "-----BEGIN CERTIFICATE-----",
            "line1",
            "line2",
            "-----END CERTIFICATE-----",
            "extra"
        };

        var filePath = Path.GetTempFileName();
        await File.WriteAllLinesAsync(filePath, pemLines);

        // Act
        var result = await _service.ReadPemContent(filePath, CancellationToken.None);

        // Assert
        var expected = string.Join(Environment.NewLine, new[]
        {
            "-----BEGIN CERTIFICATE-----",
            "line1",
            "line2",
            "-----END CERTIFICATE-----"
        });

        Assert.Equal(expected, result);

        // Cleanup
        File.Delete(filePath);
    }
    
    [Fact]
    public async Task RevokeCertificate_ReturnsSuccess_WhenRevokedSuccessfully()
    {
        // Arrange
        var commonName = "client1";
        var basePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var issuedPath = Path.Combine(basePath, "pki", "issued");
        Directory.CreateDirectory(issuedPath);

        var certPath = Path.Combine(issuedPath, $"{commonName}.crt");
        await File.WriteAllTextAsync(certPath, "dummy cert");

        _execMock
            .Setup(x => x.ExecuteEasyRsaCommand(
                $"revoke {commonName}",
                basePath,
                It.IsAny<CancellationToken>(),
                /* confirm */ true))
            .ReturnsAsync((true, "revoked", 0, ""));

        _execMock
            .Setup(x => x.ExecuteEasyRsaCommand(
                "gen-crl",
                basePath,
                It.IsAny<CancellationToken>(),
                /* confirm */ false)) // confirm явно указан
            .ReturnsAsync((true, "crl generated", 0, ""));

        var crlPath = Path.Combine(basePath, "pki", "crl.pem");
        Directory.CreateDirectory(Path.GetDirectoryName(crlPath)!);
        await File.WriteAllTextAsync(crlPath, "dummy crl");

        // Act
        var result = await _service.RevokeCertificate(basePath, commonName, CancellationToken.None);

        // Assert
        Assert.True(result.IsRevoked);
        Assert.Equal(Path.GetFullPath(result.CertificatePath), 
            Path.Combine(basePath, "pki", "issued", $"{commonName}.crt"), ignoreCase: true);
        Assert.Equal(string.Empty, result.Message);

        // Cleanup
        Directory.Delete(basePath, true);
    }
    
    [Fact]
    public async Task GetAllCertificateInfoInIndexFile_ReturnsParsedResult()
    {
        // Arrange
        var pkiPath = "/some/path/pki";
        var expectedList = new List<ServerCertificate>
        {
            new ServerCertificate { CommonName = "test1" },
            new ServerCertificate { CommonName = "test2" }
        };

        _parserMock
            .Setup(x => x.ParseCertificateInfoInIndexFileAsync(pkiPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedList);

        // Act
        var result = await _service.GetAllCertificateInfoInIndexFile(pkiPath, CancellationToken.None);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("test1", result[0].CommonName);
    }

    [Fact]
    public void InstallEasyRsa_ThrowsAndRunsInit_WhenPkiMissing()
    {
        // Arrange
        var easyRsaPath = "";

        var expectedCommand = $"cd {easyRsaPath.Replace('\\', '/')} && EASYRSA_BATCH=1 ./easyrsa init-pki";

        _execMock
            .Setup(x => x.RunCommand(expectedCommand, It.IsAny<CancellationToken>()))
            .ReturnsAsync(("", "", 0));

        // Act + Assert
        var ex = Assert.Throws<TargetInvocationException>(() =>
            _service.Invoke<object>("InstallEasyRsa", easyRsaPath, CancellationToken.None)
        );

        Assert.NotNull(ex.InnerException);
        Assert.IsType<Exception>(ex.InnerException);
        Assert.Equal("PKI directory does not exist.", ex.InnerException.Message);

    }
    
    [Fact]
    public async Task CheckCertInOpenssl_ReturnsSerial_WhenSuccess()
    {
        // Arrange
        var path = "/path/to/cert.crt";

        _execMock
            .Setup(x => x.RunCommand($"openssl x509 -in {path} -serial -noout", It.IsAny<CancellationToken>()))
            .ReturnsAsync(("serial=ABC123", "", 0));

        // Act
        var result = await _service.InvokeAsync<string>("CheckCertInOpenssl", path, CancellationToken.None);

        // Assert
        Assert.Equal("ABC123", result);
    }
    
    [Fact]
    public async Task UpdateCrl_ReturnsTrue_WhenSuccessful()
    {
        // Arrange
        var easyRsaPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var crlPath = Path.Combine(easyRsaPath, "pki", "crl.pem");
        Directory.CreateDirectory(Path.GetDirectoryName(crlPath)!);
        await File.WriteAllTextAsync(crlPath, "dummy");

        _execMock
            .Setup(x => x.ExecuteEasyRsaCommand("gen-crl", easyRsaPath, It.IsAny<CancellationToken>(), false))
            .ReturnsAsync((true, "ok", 0, ""));

        // Act
        var result = await _service.InvokeAsync<bool>("UpdateCrl", easyRsaPath, CancellationToken.None);

        // Assert
        Assert.True(result);

        // Cleanup
        Directory.Delete(easyRsaPath, true);
    }

}
