using System.Diagnostics;
using DataGateOpenVpnManager.Services.EasyRsaServices.Interfaces;

namespace DataGateOpenVpnManager.Services.EasyRsaServices;

public class BashCommandRunner : IBashCommandRunner
{
    public async Task<(string Output, int ExitCode)> RunCommandAsync(
        string command,
        Dictionary<string, string>? environmentVariables,
        CancellationToken cancellationToken,
        string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("Command cannot be null or whitespace.", nameof(command));

        if (!string.IsNullOrWhiteSpace(workingDirectory) && !Directory.Exists(workingDirectory))
            throw new DirectoryNotFoundException($"Working directory not found: {workingDirectory}");

        cancellationToken.ThrowIfCancellationRequested();
        
        // prepend environment variables as export statements
        if (environmentVariables != null && environmentVariables.Count > 0)
        {
            var exportCommands = string.Join(" ", environmentVariables.Select(kvp =>
                $"export {kvp.Key}='{kvp.Value}';"));
            command = exportCommands + " " + command;
        }

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
            await process.WaitForExitAsync(cancellationToken);
            return (output, process.ExitCode);
        }
        catch (Exception)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch {  
                // ignore errors when trying to kill the process
            }

            throw;
        }
    }
}