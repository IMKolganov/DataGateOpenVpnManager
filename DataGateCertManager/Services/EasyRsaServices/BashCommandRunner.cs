using System.Diagnostics;
using DataGateCertManager.Services.EasyRsaServices.Interfaces;

namespace DataGateCertManager.Services.EasyRsaServices;

public class BashCommandRunner : IBashCommandRunner
{
    public (string Output, string Error, int ExitCode) RunCommand(string command)
    {
        var processInfo = new ProcessStartInfo("bash", $"-c \"{command}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process == null)
            throw new InvalidOperationException("Failed to start process.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (output, error, process.ExitCode);
    }
}