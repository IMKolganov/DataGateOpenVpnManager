using DataGateCertManager.Services.EasyRsaServices;
using DataGateCertManager.Services.EasyRsaServices.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;

namespace DataGateCertManager.Tests.Services.EasyRsaServices;

public class EasyRsaExecCommandServiceTests
{
    private readonly Mock<ILogger<IEasyRsaExecCommandService>> _loggerMock = new();
    private readonly Mock<IBashCommandRunner> _bashMock = new();
    private readonly EasyRsaExecCommandService _service;

    public EasyRsaExecCommandServiceTests()
    {
        _service = new EasyRsaExecCommandService(_loggerMock.Object, _bashMock.Object);
    }

    [Fact]
    public async Task ExecuteEasyRsaCommand_ReturnsSuccess_WhenExitCodeIsZero()
    {
        // Arrange
        var path = "/some/path";
        var fullCommand = $"cd {path} && ./easyrsa gen-crl";
        var token = CancellationToken.None;

        _bashMock
            .Setup(x => x.RunCommandAsync(fullCommand, token))
            .ReturnsAsync(("CRL generated", "", 0));

        // Act
        var result = await _service.ExecuteEasyRsaCommand("gen-crl", path, token);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("CRL generated", result.Output);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
    }

    [Fact]
    public async Task ExecuteEasyRsaCommand_ReturnsFailure_WhenExitCodeIsNotZero()
    {
        // Arrange
        var path = "/test/path";
        var fullCommand = $"cd {path} && ./easyrsa revoke client1";
        var token = CancellationToken.None;

        _bashMock
            .Setup(x => x.RunCommandAsync(fullCommand, token))
            .ReturnsAsync(("", "revoke failed", 1));

        // Act
        var result = await _service.ExecuteEasyRsaCommand("revoke client1", path, token);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("revoke failed", result.Error);
    }

    [Fact]
    public async Task ExecuteEasyRsaCommand_ReturnsError_WhenExceptionThrown()
    {
        // Arrange
        var path = "/broken";
        var fullCommand = $"cd {path} && ./easyrsa broken";
        var token = CancellationToken.None;

        _bashMock
            .Setup(x => x.RunCommandAsync(fullCommand, token))
            .ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        var result = await _service.ExecuteEasyRsaCommand("broken", path, token);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(500, result.ExitCode);
        Assert.Equal("boom", result.Error);
    }

    [Fact]
    public async Task ExecuteEasyRsaCommand_Throws_WhenCancelled()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _service.ExecuteEasyRsaCommand("gen-crl", "/any", cts.Token));
    }
}
