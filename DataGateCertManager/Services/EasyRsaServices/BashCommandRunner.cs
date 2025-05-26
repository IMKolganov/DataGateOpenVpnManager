using System.Diagnostics;
using DataGateCertManager.Services.EasyRsaServices.Interfaces;

namespace DataGateCertManager.Services.EasyRsaServices;

public class BashCommandRunner : IBashCommandRunner
{
    public async Task<(string Output, int ExitCode)> RunCommandAsync(
        string command,
        Dictionary<string, string>? environmentVariables,
        CancellationToken cancellationToken,
        string? workingDirectory = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var processInfo = new ProcessStartInfo("bash", $"-c \"{command} 2>&1\"")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory()
        };

        if (environmentVariables != null)
        {
            foreach (var kvp in environmentVariables)
            {
                processInfo.Environment[kvp.Key] = kvp.Value;
            }
        }

        using var process = Process.Start(processInfo);
        if (process == null)
            throw new InvalidOperationException("Failed to start process.");

        try
        {
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);

            while (!process.HasExited)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(50, cancellationToken);
            }

            return (output, process.ExitCode);
        }
        catch (Exception)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch
            {
                // ignore errors when trying to kill the process
            }
            throw;
        }
    }
}