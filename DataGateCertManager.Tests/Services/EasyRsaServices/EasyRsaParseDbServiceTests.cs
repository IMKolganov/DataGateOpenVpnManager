using DataGateCertManager.Models.Enums;
using DataGateCertManager.Services.EasyRsaServices;
using DataGateCertManager.Services.EasyRsaServices.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;

namespace DataGateCertManager.Tests.Services.EasyRsaServices;

public class EasyRsaParseDbServiceTests
{
    private readonly Mock<ILogger<IEasyRsaParseDbService>> _loggerMock = new();
    private readonly EasyRsaParseDbService _service;

    public EasyRsaParseDbServiceTests()
    {
        _service = new EasyRsaParseDbService(_loggerMock.Object);
    }

    private string CreateTempIndexFile(string[] lines)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        var filePath = Path.Combine(tempDir, "index.txt");
        File.WriteAllLines(filePath, lines);

        return tempDir;
    }

    [Fact]
    public async Task ParseCertificateInfoInIndexFileAsync_ReturnsValidObjects()
    {
        // Arrange
        var lines = new[]
        {
            "V\t250128120000Z\t\t01\tunknown\t/CN=test1",
            "R\t240101010101Z\t230101010101Z\t02\tunknown\t/CN=test2"
        };

        var path = CreateTempIndexFile(lines);

        // Act
        var result = await _service.ParseCertificateInfoInIndexFileAsync(path, CancellationToken.None);

        // Assert
        Assert.Equal(2, result.Count);

        Assert.Equal("test1", result[0].CommonName);
        Assert.Equal(CertificateStatus.Active, result[0].Status);
        Assert.Equal(
            DateTime.SpecifyKind(new DateTime(2025, 1, 28, 12, 0, 0), DateTimeKind.Utc),
            result[0].ExpiryDate.ToUniversalTime());

        Assert.Equal("test2", result[1].CommonName);
        Assert.Equal(CertificateStatus.Revoked, result[1].Status);
        Assert.Equal(new DateTime(2024, 1, 1, 1, 1, 1, DateTimeKind.Utc), 
            result[1].ExpiryDate.ToUniversalTime());
        Assert.Equal(
            DateTime.SpecifyKind(new DateTime(2023, 1, 1, 1, 1, 1), DateTimeKind.Utc),
            result[1].RevokeDate!.Value.ToUniversalTime());

    }

    [Fact]
    public async Task ParseCertificateInfoInIndexFileAsync_ThrowsFormatException_OnInvalidDate()
    {
        var lines = new[]
        {
            "V\tbad-date\t\t01\tunknown\t/CN=bad"
        };

        var path = CreateTempIndexFile(lines);

        var ex = await Assert.ThrowsAsync<FormatException>(() =>
            _service.ParseCertificateInfoInIndexFileAsync(path, CancellationToken.None));

        Assert.Contains("Invalid date format", ex.Message);
    }

    [Fact]
    public async Task ParseCertificateInfoInIndexFileAsync_IgnoresInvalidLines()
    {
        var lines = new[]
        {
            "invalid line with tabs\tbut not enough fields",
            "V\t250101000000Z\t\t03\tunknown\t/CN=valid"
        };

        var path = CreateTempIndexFile(lines);

        var result = await _service.ParseCertificateInfoInIndexFileAsync(path, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("valid", result[0].CommonName);
    }

    [Fact]
    public async Task ParseCertificateInfoInIndexFileAsync_Throws_WhenCancelled()
    {
        var lines = new[]
        {
            "V\t250101000000Z\t\t03\tunknown\t/CN=test"
        };

        var path = CreateTempIndexFile(lines);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        
        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            _service.ParseCertificateInfoInIndexFileAsync(path, cts.Token));
    }
}
