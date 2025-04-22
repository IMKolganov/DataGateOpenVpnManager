using DataGateCertManager.Services.EasyRsaServices;

namespace DataGateCertManager.Tests.Services.EasyRsaServices;

public class BashCommandRunnerTests
{
    private readonly BashCommandRunner _runner = new();

    [Fact]
    public async Task RunCommandAsync_ReturnsOutput_WhenCommandSucceeds()
    {
        // Arrange
        var command = "echo hello";

        // Act
        var (output, error, exitCode) = await _runner.RunCommandAsync(command);

        // Assert
        Assert.Equal("hello\n", output); // Bash echo adds a newline
        Assert.Equal(string.Empty, error);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task RunCommandAsync_ReturnsError_WhenCommandFails()
    {
        // Arrange
        var command = "ls /path/that/does/not/exist";

        // Act
        var (output, error, exitCode) = await _runner.RunCommandAsync(command);

        // Assert
        Assert.Equal(string.Empty, output);
        Assert.NotEmpty(error);
        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public async Task RunCommandAsync_Throws_WhenCancelled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _runner.RunCommandAsync("sleep 1", cts.Token));
    }
}
