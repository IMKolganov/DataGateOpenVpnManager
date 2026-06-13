using DataGateOpenVpnManager.Services.EasyRsaServices;
using DataGateOpenVpnManager.Services.EasyRsaServices.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Cert.Responses;
using DataGateMonitor.SharedModels.Enums;

namespace DataGateOpenVpnManager.Tests.Services.EasyRsaServices;

public class EasyRsaParseDbServiceTests
{
    private readonly Mock<ILogger<IEasyRsaParseDbService>> _loggerMock = new();

    [Fact]
    public async Task ParseCertificateInfoInIndexFileAsync_WhenFileExists_ParsesLines()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "EasyRsaParseDb_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var indexPath = Path.Combine(tempDir, "index.txt");
        // Format: status \t expiry \t revoke \t serial \t unknown \t /CN=name (year 40 = 2040; avoid DateTime.MinValue for RevokeDate)
        await File.WriteAllLinesAsync(indexPath, new[]
        {
            "R\t400128120000Z\t400201100000Z\t01\tunknown\t/CN=revoked1",
            "R\t400128120000Z\t400201100000Z\t02\tunknown\t/CN=revoked2"
        });

        try
        {
            var service = new EasyRsaParseDbService(_loggerMock.Object);
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
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ParseCertificateInfoInIndexFileAsync_WhenFileMissing_ThrowsFileNotFoundException()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "EasyRsaParseDb_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        // do not create index.txt

        try
        {
            var service = new EasyRsaParseDbService(_loggerMock.Object);

            var ex = await Assert.ThrowsAsync<FileNotFoundException>(() =>
                service.ParseCertificateInfoInIndexFileAsync(tempDir, CancellationToken.None));

            Assert.Contains("index.txt", ex.Message);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ParseCertificateInfoInIndexFileAsync_WhenLineHasFewerThanSixParts_SkipsLine()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "EasyRsaParseDb_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var indexPath = Path.Combine(tempDir, "index.txt");
        await File.WriteAllLinesAsync(indexPath, new[] { "V\t250128120000Z\t\t01" });

        try
        {
            var service = new EasyRsaParseDbService(_loggerMock.Object);
            var result = await service.ParseCertificateInfoInIndexFileAsync(tempDir, CancellationToken.None);

            Assert.Empty(result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
