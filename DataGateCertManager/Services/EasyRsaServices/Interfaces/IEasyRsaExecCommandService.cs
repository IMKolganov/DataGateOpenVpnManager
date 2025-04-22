namespace DataGateCertManager.Services.EasyRsaServices.Interfaces;

public interface IEasyRsaExecCommandService
{
    Task<(bool IsSuccess, string Output, int ExitCode, string Error)> ExecuteEasyRsaCommand(string arguments,
        string easyRsaPath,
        CancellationToken cancellationToken,
        bool confirm = false);
        
    Task<(string Output, string Error, int ExitCode)> RunCommand(string command,
        CancellationToken cancellationToken);
}