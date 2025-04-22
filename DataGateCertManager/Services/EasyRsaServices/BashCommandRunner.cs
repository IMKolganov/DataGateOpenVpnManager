using System.Diagnostics;
using DataGateCertManager.Services.EasyRsaServices.Interfaces;

namespace DataGateCertManager.Services.EasyRsaServices;

public class BashCommandRunner : IBashCommandRunner
{
    public async Task<(string Output, string Error, int ExitCode)> RunCommandAsync(
        string command,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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

        try
        {
            var readOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var readErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            while (!process.HasExited)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(100, cancellationToken);
            }

            var output = await readOutputTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            var error = await readErrorTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

            return (output, error, process.ExitCode);
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
                // ignore errors when try to kill the process
            }
            throw;
        }
    }
}