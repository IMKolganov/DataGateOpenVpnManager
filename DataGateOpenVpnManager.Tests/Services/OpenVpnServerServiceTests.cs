using DataGateOpenVpnManager.Services;
using DataGateOpenVpnManager.Services.EasyRsaServices.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;

namespace DataGateOpenVpnManager.Tests.Services;

public class OpenVpnServerServiceTests
{
    [Fact]
    public async Task BuildTlsAuthKeyAsync_WhenCommandSucceeds_ReturnsTaKeyPath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "OpenVpnServerServiceTests_" + Guid.NewGuid().ToString("N"));
        var pkiDir = Path.Combine(tempDir, "pki");
        Directory.CreateDirectory(pkiDir);

        try
        {
            var mockRunner = new Mock<IBashCommandRunner>();
            mockRunner
                .Setup(r => r.RunCommandAsync(
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()))
                .ReturnsAsync(("", 0));

            var taKeyName = "ta.key";
            var taKeyPath = Path.Combine(pkiDir, taKeyName);
            await File.WriteAllTextAsync(taKeyPath, "test key content");

            var loggerMock = new Mock<ILogger<OpenVpnServerService>>();
            var service = new OpenVpnServerService(loggerMock.Object, mockRunner.Object);

            var result = await service.BuildTlsAuthKeyAsync(tempDir, taKeyName, CancellationToken.None);

            Assert.Equal(Path.GetFullPath(taKeyPath), result);
            mockRunner.Verify(
                r => r.RunCommandAsync(
                    It.Is<string>(c => c.Contains("openvpn") && c.Contains("genkey")),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>(),
                    It.Is<string>(w => w == pkiDir)),
                Times.Once);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task BuildTlsAuthKeyAsync_WhenCommandReturnsNonZero_Throws()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "OpenVpnServerServiceTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var mockRunner = new Mock<IBashCommandRunner>();
            mockRunner
                .Setup(r => r.RunCommandAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>(), It.IsAny<string>()))
                .ReturnsAsync(("error output", 1));

            var loggerMock = new Mock<ILogger<OpenVpnServerService>>();
            var service = new OpenVpnServerService(loggerMock.Object, mockRunner.Object);

            var ex = await Assert.ThrowsAsync<Exception>(() =>
                service.BuildTlsAuthKeyAsync(tempDir, "ta.key", CancellationToken.None));

            Assert.Contains("TLS-auth", ex.Message);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task BuildTlsAuthKeyAsync_WhenKeyFileNotCreated_ThrowsFileNotFoundException()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "OpenVpnServerServiceTests_" + Guid.NewGuid().ToString("N"));
        var pkiDir = Path.Combine(tempDir, "pki");
        Directory.CreateDirectory(pkiDir);

        try
        {
            var mockRunner = new Mock<IBashCommandRunner>();
            mockRunner
                .Setup(r => r.RunCommandAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>(), It.IsAny<string>()))
                .ReturnsAsync(("", 0));
            // do NOT create ta.key so File.Exists fails

            var loggerMock = new Mock<ILogger<OpenVpnServerService>>();
            var service = new OpenVpnServerService(loggerMock.Object, mockRunner.Object);

            var ex = await Assert.ThrowsAsync<FileNotFoundException>(() =>
                service.BuildTlsAuthKeyAsync(tempDir, "ta.key", CancellationToken.None));

            Assert.Contains("TLS-auth", ex.Message);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
