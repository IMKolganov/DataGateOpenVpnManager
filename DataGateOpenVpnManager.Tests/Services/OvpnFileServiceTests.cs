using DataGateOpenVpnManager.Models;
using DataGateOpenVpnManager.Services;
using DataGateOpenVpnManager.Services.EasyRsaServices.Interfaces;
using DataGateOpenVpnManager.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Cert.Responses;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.OvpnFile.Responses;

namespace DataGateOpenVpnManager.Tests.Services;

public class OvpnFileServiceTests
{
    private readonly Mock<ILogger<IOvpnFileService>> _loggerMock = new();
    private readonly Mock<IEasyRsaService> _easyRsaMock = new();
    private readonly IOptions<EasyRsaOptions> _options = Options.Create(new EasyRsaOptions());

    [Fact]
    public async Task RevokeOvpnFile_WhenFileExists_MovesFileAndReturnsMetadata()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "OvpnRevoke_" + Guid.NewGuid().ToString("N"));
        var ovpnPath = Path.Combine(tempDir, "client1.ovpn");
        Directory.CreateDirectory(tempDir);
        await File.WriteAllTextAsync(ovpnPath, "client");

        _easyRsaMock.Setup(s => s.RevokeCertificateAsync(It.IsAny<string>(), "client1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServerCertificate { Message = "Revoked", CertificatePath = "/pki/revoked/client1.crt", KeyPath = "/pki/private/client1.key" });

        var service = new OvpnFileService(_loggerMock.Object, _easyRsaMock.Object, _options);

        try
        {
            var result = await service.RevokeOvpnFile(tempDir, "client1", "client1.ovpn", ovpnPath, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal("client1", result.CommonName);
            Assert.Equal("client1.ovpn", result.FileName);
            Assert.Contains("revoked", result.FilePath);
            Assert.False(File.Exists(ovpnPath));
            var revokedDir = Path.Combine(tempDir, "pki", "revoked", "ovpn_files");
            Assert.True(Directory.Exists(revokedDir));
            Assert.Single(Directory.GetFiles(revokedDir));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task RevokeOvpnFile_WhenFileMissing_StillReturnsMetadata()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "OvpnRevoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var missingPath = Path.Combine(tempDir, "missing.ovpn");

        _easyRsaMock.Setup(s => s.RevokeCertificateAsync(It.IsAny<string>(), "client1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServerCertificate { Message = "Revoked" });

        var service = new OvpnFileService(_loggerMock.Object, _easyRsaMock.Object, _options);

        try
        {
            var result = await service.RevokeOvpnFile(tempDir, "client1", "missing.ovpn", missingPath, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal("client1", result.CommonName);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetOvpnFile_WhenFileExists_ReturnsContent()
    {
        var tempFile = Path.GetTempFileName();
        var expectedContent = "client\nremote 1.2.3.4 1194";
        await File.WriteAllTextAsync(tempFile, expectedContent);

        try
        {
            var service = new OvpnFileService(_loggerMock.Object, _easyRsaMock.Object, _options);
            var result = await service.GetOvpnFile("test.ovpn", tempFile, CancellationToken.None);

            Assert.Equal("test.ovpn", result.FileName);
            Assert.NotNull(result.Content);
            var text = System.Text.Encoding.UTF8.GetString(result.Content);
            Assert.Equal(expectedContent, text);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GetOvpnFile_WhenFileDoesNotExist_Throws()
    {
        var service = new OvpnFileService(_loggerMock.Object, _easyRsaMock.Object, _options);
        var path = Path.Combine(Path.GetTempPath(), "nonexistent_" + Guid.NewGuid().ToString("N") + ".ovpn");

        var ex = await Assert.ThrowsAsync<Exception>(() =>
            service.GetOvpnFile("nope.ovpn", path, CancellationToken.None));

        Assert.Contains("does not exist", ex.Message);
    }
}
