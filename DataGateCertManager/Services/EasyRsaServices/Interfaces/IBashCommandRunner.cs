namespace DataGateCertManager.Services.EasyRsaServices.Interfaces;

public interface IBashCommandRunner
{
    Task<(string Output, string Error, int ExitCode)> RunCommandAsync(
        string command,
        Dictionary<string, string>? environmentVariables,
        CancellationToken cancellationToken,
        string? workingDirectory = null);
}