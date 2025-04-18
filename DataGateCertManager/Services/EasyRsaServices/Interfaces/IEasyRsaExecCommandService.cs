namespace DataGateCertManager.Services.EasyRsaServices.Interfaces;

public interface IEasyRsaExecCommandService
{
    (bool IsSuccess, string Output, int ExitCode, string Error) ExecuteEasyRsaCommand(string arguments,
        string easyRsaPath, bool confirm = false);
    (string Output, string Error, int ExitCode) RunCommand(string command);
}