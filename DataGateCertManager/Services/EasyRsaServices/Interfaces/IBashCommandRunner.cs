namespace DataGateCertManager.Services.EasyRsaServices.Interfaces;

public interface IBashCommandRunner
{
    (string Output, string Error, int ExitCode) RunCommand(string command);
}