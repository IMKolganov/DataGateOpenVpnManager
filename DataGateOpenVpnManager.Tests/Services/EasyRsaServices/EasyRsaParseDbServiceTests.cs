using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DataGateOpenVpnManager.Models;
using DataGateOpenVpnManager.Services.EasyRsaServices;
using DataGateOpenVpnManager.Services.EasyRsaServices.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using DataGateMonitor.SharedModels.Enums;

namespace DataGateOpenVpnManager.Tests.Services.EasyRsaServices;

public class EasyRsaParseDbServiceTests
{
    private readonly Mock<ILogger<IEasyRsaParseDbService>> _loggerMock = new();

    private EasyRsaParseDbService CreateService(EasyRsaOptions? options = null) =>
        new(_loggerMock.Object, Options.Create(options ?? new EasyRsaOptions()));

    [Fact]
    public async Task ParseCertificateInfoInIndexFileAsync_WhenFileExists_ParsesLines()
    {
        var tempDir = CreatePkiRoot(out _);
        var indexPath = Path.Combine(tempDir, "index.txt");
        await File.WriteAllLinesAsync(indexPath, new[]
        {
            "R\t400128120000Z\t400201100000Z\t01\tunknown\t/CN=revoked1",
            "R\t400128120000Z\t400201100000Z\t02\tunknown\t/CN=revoked2"
        });

        try
        {
            var service = CreateService();
            var result = await service.ParseCertificateInfoInIndexFileAsync(tempDir, CancellationToken.None);

            Assert.Equal(2, result.Count);

            var first = result[0];
            Assert.Equal("revoked1", first.CommonName);
            Assert.Equal(CertificateStatus.Revoked, first.Status);
            Assert.True(first.IsRevoked);
            Assert.Equal("01", first.SerialNumber);

            var second = result[1];
            Assert.Equal("revoked2", second.CommonName);
            Assert.Equal(CertificateStatus.Revoked, second.Status);
            Assert.Equal("02", second.SerialNumber);
        }
        finally
        {
            DeletePkiRoot(tempDir);
        }
    }

    [Fact]
    public async Task ParseCertificateInfoInIndexFileAsync_WhenFileMissing_ThrowsFileNotFoundException()
    {
        var tempDir = CreatePkiRoot(out _);

        try
        {
            var service = CreateService();

            var ex = await Assert.ThrowsAsync<FileNotFoundException>(() =>
                service.ParseCertificateInfoInIndexFileAsync(tempDir, CancellationToken.None));

            Assert.Contains("index.txt", ex.Message);
        }
        finally
        {
            DeletePkiRoot(tempDir);
        }
    }

    [Fact]
    public async Task ParseCertificateInfoInIndexFileAsync_WhenLineHasFewerThanSixParts_SkipsLine()
    {
        var tempDir = CreatePkiRoot(out _);
        var indexPath = Path.Combine(tempDir, "index.txt");
        await File.WriteAllLinesAsync(indexPath, new[] { "V\t250128120000Z\t\t01" });

        try
        {
            var service = CreateService();
            var result = await service.ParseCertificateInfoInIndexFileAsync(tempDir, CancellationToken.None);

            Assert.Empty(result);
        }
        finally
        {
            DeletePkiRoot(tempDir);
        }
    }

    [Fact]
    public async Task ParseCertificateInfoInIndexFileAsync_CaEntry_ResolvesCaPathsWithoutWarnings()
    {
        var tempDir = CreatePkiRoot(out var caSerial);
        var indexPath = Path.Combine(tempDir, "index.txt");
        await File.WriteAllLinesAsync(indexPath, new[]
        {
            $"V\t400128120000Z\t\t{caSerial}\tunknown\t/CN=OpenVPN-Server"
        });

        try
        {
            var service = CreateService();
            var result = await service.ParseCertificateInfoInIndexFileAsync(tempDir, CancellationToken.None);

            Assert.Single(result);
            Assert.Equal(Path.Combine(tempDir, "ca.crt"), result[0].CertificatePath);
            Assert.Equal(Path.Combine(tempDir, "private", "ca.key"), result[0].KeyPath);

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Never);
        }
        finally
        {
            DeletePkiRoot(tempDir);
        }
    }

    [Fact]
    public async Task ParseCertificateInfoInIndexFileAsync_CaEntryWithCustomCn_ResolvesBySerialMatch()
    {
        const string caCn = "My-Custom-CA-Name";
        var tempDir = CreatePkiRoot(out var caSerial, caCn);
        var indexPath = Path.Combine(tempDir, "index.txt");
        await File.WriteAllLinesAsync(indexPath, new[]
        {
            $"V\t400128120000Z\t\t{caSerial}\tunknown\t/CN={caCn}"
        });

        try
        {
            var service = CreateService();
            var result = await service.ParseCertificateInfoInIndexFileAsync(tempDir, CancellationToken.None);

            Assert.Equal(Path.Combine(tempDir, "ca.crt"), result[0].CertificatePath);
            Assert.Equal(Path.Combine(tempDir, "private", "ca.key"), result[0].KeyPath);
        }
        finally
        {
            DeletePkiRoot(tempDir);
        }
    }

    [Fact]
    public async Task ParseCertificateInfoInIndexFileAsync_ActiveClient_UsesCertsBySerialWhenIssuedMissing()
    {
        var tempDir = CreatePkiRoot(out _);
        Directory.CreateDirectory(Path.Combine(tempDir, "certs_by_serial"));
        Directory.CreateDirectory(Path.Combine(tempDir, "private"));

        const string serial = "E28F6B5AE4BDAC22CD9B529921CEA960";
        const string cn = "adg-75-106529657373562471831-n-Saz_jhQAGH1FuTPho1Xg";
        await File.WriteAllTextAsync(Path.Combine(tempDir, "certs_by_serial", $"{serial}.pem"), "dummy-pem");
        await File.WriteAllTextAsync(Path.Combine(tempDir, "private", $"{cn}.key"), "dummy-key");
        await File.WriteAllLinesAsync(Path.Combine(tempDir, "index.txt"), new[]
        {
            $"V\t400128120000Z\t\t{serial}\tunknown\t/CN={cn}"
        });

        try
        {
            var service = CreateService();
            var result = await service.ParseCertificateInfoInIndexFileAsync(tempDir, CancellationToken.None);

            Assert.Equal(Path.Combine(tempDir, "certs_by_serial", $"{serial}.pem"), result[0].CertificatePath);
            Assert.Equal(Path.Combine(tempDir, "private", $"{cn}.key"), result[0].KeyPath);

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Never);
        }
        finally
        {
            DeletePkiRoot(tempDir);
        }
    }

    [Fact]
    public async Task ParseCertificateInfoInIndexFileAsync_ServerEntry_ResolvesServerPaths()
    {
        var tempDir = CreatePkiRoot(out _);
        Directory.CreateDirectory(Path.Combine(tempDir, "issued"));
        Directory.CreateDirectory(Path.Combine(tempDir, "private"));
        await File.WriteAllTextAsync(Path.Combine(tempDir, "issued", "server.crt"), "server-cert");
        await File.WriteAllTextAsync(Path.Combine(tempDir, "private", "server.key"), "server-key");
        await File.WriteAllLinesAsync(Path.Combine(tempDir, "index.txt"), new[]
        {
            "V\t400128120000Z\t\t9BC3C44B3152A000552DD79B9C424A35\tunknown\t/CN=server"
        });

        try
        {
            var service = CreateService();
            var result = await service.ParseCertificateInfoInIndexFileAsync(tempDir, CancellationToken.None);

            Assert.Equal(Path.Combine(tempDir, "issued", "server.crt"), result[0].CertificatePath);
            Assert.Equal(Path.Combine(tempDir, "private", "server.key"), result[0].KeyPath);
        }
        finally
        {
            DeletePkiRoot(tempDir);
        }
    }

    [Fact]
    public async Task ParseCertificateInfoInIndexFileAsync_FullEasyRsaLayout_ResolvesCaServerAndClient()
    {
        var tempDir = CreatePkiRoot(out var caSerial);
        Directory.CreateDirectory(Path.Combine(tempDir, "issued"));
        Directory.CreateDirectory(Path.Combine(tempDir, "private"));

        const string clientCn = "adg-75-106529657373562471831-n-Saz_jhQAGH1FuTPho1Xg";
        const string clientSerial = "0495F0103746B13EFED0BE0BB149C4EA";
        const string serverSerial = "9BC3C44B3152A000552DD79B9C424A35";

        await File.WriteAllTextAsync(Path.Combine(tempDir, "issued", "server.crt"), "server-cert");
        await File.WriteAllTextAsync(Path.Combine(tempDir, "private", "server.key"), "server-key");
        await File.WriteAllTextAsync(Path.Combine(tempDir, "issued", $"{clientCn}.crt"), "client-cert");
        await File.WriteAllTextAsync(Path.Combine(tempDir, "private", $"{clientCn}.key"), "client-key");

        await File.WriteAllLinesAsync(Path.Combine(tempDir, "index.txt"), new[]
        {
            $"V\t400128120000Z\t\t{caSerial}\tunknown\t/CN=OpenVPN-Server",
            $"V\t400128120000Z\t\t{serverSerial}\tunknown\t/CN=server",
            $"V\t400128120000Z\t\t{clientSerial}\tunknown\t/CN={clientCn}",
        });

        try
        {
            var service = CreateService();
            var result = await service.ParseCertificateInfoInIndexFileAsync(tempDir, CancellationToken.None);

            Assert.Equal(3, result.Count);

            var ca = result.Single(r => r.CommonName == "OpenVPN-Server");
            Assert.Equal(Path.Combine(tempDir, "ca.crt"), ca.CertificatePath);
            Assert.Equal(Path.Combine(tempDir, "private", "ca.key"), ca.KeyPath);

            var server = result.Single(r => r.CommonName == "server");
            Assert.Equal(Path.Combine(tempDir, "issued", "server.crt"), server.CertificatePath);
            Assert.Equal(Path.Combine(tempDir, "private", "server.key"), server.KeyPath);

            var client = result.Single(r => r.CommonName == clientCn);
            Assert.Equal(Path.Combine(tempDir, "issued", $"{clientCn}.crt"), client.CertificatePath);
            Assert.Equal(Path.Combine(tempDir, "private", $"{clientCn}.key"), client.KeyPath);

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Never);
        }
        finally
        {
            DeletePkiRoot(tempDir);
        }
    }

    [Fact]
    public async Task ParseCertificateInfoInIndexFileAsync_ActiveClient_PrefersIssuedOverCertsBySerial()
    {
        var tempDir = CreatePkiRoot(out _);
        Directory.CreateDirectory(Path.Combine(tempDir, "issued"));
        Directory.CreateDirectory(Path.Combine(tempDir, "certs_by_serial"));
        Directory.CreateDirectory(Path.Combine(tempDir, "private"));

        const string serial = "0495F0103746B13EFED0BE0BB149C4EA";
        const string cn = "client-with-both-paths";
        await File.WriteAllTextAsync(Path.Combine(tempDir, "issued", $"{cn}.crt"), "issued-cert");
        await File.WriteAllTextAsync(Path.Combine(tempDir, "certs_by_serial", $"{serial}.pem"), "serial-pem");
        await File.WriteAllTextAsync(Path.Combine(tempDir, "private", $"{cn}.key"), "client-key");
        await File.WriteAllLinesAsync(Path.Combine(tempDir, "index.txt"), new[]
        {
            $"V\t400128120000Z\t\t{serial}\tunknown\t/CN={cn}"
        });

        try
        {
            var service = CreateService();
            var result = await service.ParseCertificateInfoInIndexFileAsync(tempDir, CancellationToken.None);

            Assert.Equal(Path.Combine(tempDir, "issued", $"{cn}.crt"), result[0].CertificatePath);
        }
        finally
        {
            DeletePkiRoot(tempDir);
        }
    }

    [Fact]
    public async Task ParseCertificateInfoInIndexFileAsync_RevokedClient_UsesRevokedDirectory()
    {
        var tempDir = CreatePkiRoot(out _);
        Directory.CreateDirectory(Path.Combine(tempDir, "certs_by_serial"));

        const string cn = "revoked-client";
        const string serial = "DEADBEEF";
        await File.WriteAllTextAsync(Path.Combine(tempDir, "certs_by_serial", $"{serial}.pem"), "revoked-cert-pem");
        await File.WriteAllLinesAsync(Path.Combine(tempDir, "index.txt"), new[]
        {
            $"R\t400128120000Z\t400201100000Z\t{serial}\tunknown\t/CN={cn}"
        });

        try
        {
            var service = CreateService();
            var result = await service.ParseCertificateInfoInIndexFileAsync(tempDir, CancellationToken.None);

            Assert.Equal(Path.Combine(tempDir, "certs_by_serial", $"{serial}.pem"), result[0].CertificatePath);
            Assert.Equal(string.Empty, result[0].KeyPath);
            Assert.True(result[0].IsRevoked);
            Assert.NotNull(result[0].RevokeDate);
        }
        finally
        {
            DeletePkiRoot(tempDir);
        }
    }

    [Fact]
    public async Task ParseCertificateInfoInIndexFileAsync_RevokedClientWithoutFiles_DoesNotWarn()
    {
        var tempDir = CreatePkiRoot(out _);
        const string cn = "adg-75-106529657373562471831-n-Saz_jhQAGH1FuTPho1Xg";
        await File.WriteAllLinesAsync(Path.Combine(tempDir, "index.txt"), new[]
        {
            $"R\t400128120000Z\t400201100000Z\tE28F6B5AE4BDAC22CD9B529921CEA960\tunknown\t/CN={cn}",
            $"R\t400128120000Z\t400201100000Z\t0495F0103746B13EFED0BE0BB149C4EA\tunknown\t/CN={cn}",
        });

        try
        {
            var service = CreateService();
            var result = await service.ParseCertificateInfoInIndexFileAsync(tempDir, CancellationToken.None);

            Assert.Equal(2, result.Count);
            Assert.All(result, r =>
            {
                Assert.Equal(cn, r.CommonName);
                Assert.True(r.IsRevoked);
                Assert.Equal(string.Empty, r.CertificatePath);
                Assert.Equal(string.Empty, r.KeyPath);
            });

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Never);
        }
        finally
        {
            DeletePkiRoot(tempDir);
        }
    }

    [Fact]
    public async Task ParseCertificateInfoInIndexFileAsync_RevokedClient_UsesRevokedCertsBySerialDirectory()
    {
        var tempDir = CreatePkiRoot(out _);
        Directory.CreateDirectory(Path.Combine(tempDir, "revoked", "certs_by_serial"));

        const string cn = "revoked-client";
        const string serial = "0495F0103746B13EFED0BE0BB149C4EA";
        await File.WriteAllTextAsync(
            Path.Combine(tempDir, "revoked", "certs_by_serial", $"{serial}.crt"),
            "revoked-by-serial");
        await File.WriteAllLinesAsync(Path.Combine(tempDir, "index.txt"), new[]
        {
            $"R\t400128120000Z\t400201100000Z\t{serial}\tunknown\t/CN={cn}"
        });

        try
        {
            var service = CreateService();
            var result = await service.ParseCertificateInfoInIndexFileAsync(tempDir, CancellationToken.None);

            Assert.Equal(
                Path.Combine(tempDir, "revoked", "certs_by_serial", $"{serial}.crt"),
                result[0].CertificatePath);
        }
        finally
        {
            DeletePkiRoot(tempDir);
        }
    }

    [Fact]
    public async Task ParseCertificateInfoInIndexFileAsync_MissingClientCert_LogsWarningAndReturnsEmptyPaths()
    {
        var tempDir = CreatePkiRoot(out _);
        await File.WriteAllLinesAsync(Path.Combine(tempDir, "index.txt"), new[]
        {
            "V\t400128120000Z\t\tABCDEF01\tunknown\t/CN=ghost-client"
        });

        try
        {
            var service = CreateService();
            var result = await service.ParseCertificateInfoInIndexFileAsync(tempDir, CancellationToken.None);

            Assert.Equal("ghost-client", result[0].CommonName);
            Assert.Equal(string.Empty, result[0].CertificatePath);
            Assert.Equal(string.Empty, result[0].KeyPath);

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Certificate file not found")),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
        finally
        {
            DeletePkiRoot(tempDir);
        }
    }

    [Fact]
    public async Task ParseCertificateInfoInIndexFileAsync_ActiveClientWithIssuedPath_DoesNotMisclassifyAsCa()
    {
        var tempDir = CreatePkiRoot(out _);
        Directory.CreateDirectory(Path.Combine(tempDir, "issued"));
        Directory.CreateDirectory(Path.Combine(tempDir, "private"));

        const string cn = "adg-77-115167259820649024484-R2v3RK01QLOPCQWJvgOesg";
        await File.WriteAllTextAsync(Path.Combine(tempDir, "issued", $"{cn}.crt"), "client-cert");
        await File.WriteAllTextAsync(Path.Combine(tempDir, "private", $"{cn}.key"), "client-key");
        await File.WriteAllLinesAsync(Path.Combine(tempDir, "index.txt"), new[]
        {
            "V\t400128120000Z\t\t7093AD7E6BA05A7D25CE8A039B1C55E5\tunknown\t/CN=" + cn
        });

        try
        {
            var service = CreateService();
            var result = await service.ParseCertificateInfoInIndexFileAsync(tempDir, CancellationToken.None);

            Assert.Equal(Path.Combine(tempDir, "issued", $"{cn}.crt"), result[0].CertificatePath);
            Assert.NotEqual(Path.Combine(tempDir, "ca.crt"), result[0].CertificatePath);
        }
        finally
        {
            DeletePkiRoot(tempDir);
        }
    }

    private static string CreatePkiRoot(out string caSerial, string caCommonName = "OpenVPN-Server")
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "EasyRsaParseDb_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(Path.Combine(tempDir, "private"));

        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={caCommonName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        caSerial = cert.SerialNumber;

        File.WriteAllText(Path.Combine(tempDir, "ca.crt"), cert.ExportCertificatePem());
        File.WriteAllText(Path.Combine(tempDir, "private", "ca.key"), rsa.ExportPkcs8PrivateKeyPem());

        return tempDir;
    }

    private static void DeletePkiRoot(string tempDir)
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }
}
