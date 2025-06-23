using System.Runtime.InteropServices;
using DataGateCertManager.Services.EasyRsaServices;

namespace DataGateCertManager.Tests.Services.EasyRsaServices;

public class BashCommandRunnerTests
{
    private readonly BashCommandRunner _commandRunner;

    public BashCommandRunnerTests()
    {
        _commandRunner = new BashCommandRunner();
    }

    [Fact]
    public async Task RunCommandAsync_SuccessfulCommand_ReturnsOutputAndZeroExitCode()
    {
        // Arrange
        var command = "echo 'Hello World'";

        // Act
        var (output, exitCode) = await _commandRunner.RunCommandAsync(
            command,
            null,
            CancellationToken.None);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains("Hello World", output.Trim());
    }

    [Fact]
    public async Task RunCommandAsync_WithWorkingDirectory_UsesCorrectDirectory()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;
        
        // Arrange
        var tempDir = Path.GetTempPath();
        var command = "pwd";

        // Act
        var (output, exitCode) = await _commandRunner.RunCommandAsync(
            command,
            null,
            CancellationToken.None,
            tempDir);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains(tempDir.TrimEnd(Path.DirectorySeparatorChar), 
            output.Trim().TrimEnd(Path.DirectorySeparatorChar));
    }


    [Fact]
    public async Task RunCommandAsync_WithCancelledToken_ThrowsCancellationException()
    {
        // Arrange
        var command = "sleep 10";
        var cts = new CancellationTokenSource();

        // Act
        var commandTask = _commandRunner.RunCommandAsync(command, null, cts.Token);
        cts.Cancel();

        // Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => commandTask);
    }

    [Fact]
    public async Task RunCommandAsync_WithStandardError_CapturesErrorOutput()
    {
        // Arrange
        var command = "ls /nonexistent/directory";

        // Act
        var (output, exitCode) = await _commandRunner.RunCommandAsync(
            command,
            null,
            CancellationToken.None);

        // Assert
        Assert.NotEqual(0, exitCode);
        Assert.Contains("No such file or directory", output);
    }

    [Fact]
    public async Task RunCommandAsync_WithLongRunningCommand_CanBeCompleted()
    {
        // Arrange
        var command = "sleep 2";

        // Act
        var (output, exitCode) = await _commandRunner.RunCommandAsync(
            command,
            null,
            CancellationToken.None);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Empty(output.Trim());
    }

    [Fact]
    public async Task RunCommandAsync_WithInvalidCommand_ReturnsNonZeroExitCode()
    {
        // Arrange
        var command = "invalidcommand";

        // Act
        var (output, exitCode) = await _commandRunner.RunCommandAsync(
            command,
            null,
            CancellationToken.None);

        // Assert
        Assert.NotEqual(0, exitCode);
        Assert.Contains("command not found", output.ToLower());
    }

    [Fact]
    public async Task RunCommandAsync_WithNonExistentWorkingDirectory_ThrowsException()
    {
        // Arrange
        var command = "echo test";
        var nonExistentDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act & Assert
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            _commandRunner.RunCommandAsync(command, null, CancellationToken.None, nonExistentDirectory));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RunCommandAsync_WithInvalidCommandText_ThrowsArgumentException(string command)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _commandRunner.RunCommandAsync(command, null, CancellationToken.None));
    }
}