namespace DataGateCertManager.Services.EasyRsaServices.Interfaces;

public interface IBashCommandRunner
{
    Task<(string Output, string Error, int ExitCode)> RunCommandAsync(
        string command, 
        CancellationToken cancellationToken = default);
}