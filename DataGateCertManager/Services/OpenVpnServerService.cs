using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DataGateCertManager.Services.EasyRsaServices.Interfaces;
using DataGateCertManager.Services.Interfaces;

namespace DataGateCertManager.Services;

public class OpenVpnServerService(ILogger<OpenVpnServerService> logger, 
    IBashCommandRunner easyRsaExecCommandService) : IOpenVpnServerService
{
    public async Task<string> BuildTlsAuthKeyAsync(string easyRsaPath, string taKeyName,
        CancellationToken cancellationToken)
    {
        easyRsaPath = Path.GetFullPath(easyRsaPath);
        var pkiDir = Path.Combine(easyRsaPath, "pki");
        var taKeyPath = Path.Combine(pkiDir, taKeyName);

        if (!Directory.Exists(pkiDir))
        {
            Directory.CreateDirectory(pkiDir);
        }

        logger.LogInformation("Generating TLS-auth key: {Path}", taKeyPath);

        var command = $"openvpn --genkey --secret \"{taKeyName}\"";

        var (output, exitCode) = await easyRsaExecCommandService.RunCommandAsync(
            command,
            environmentVariables: new Dictionary<string, string>(),
            cancellationToken: cancellationToken,
            workingDirectory: pkiDir);

        if (exitCode != 0)
        {
            logger.LogError("Failed to generate TLS-auth key.\nOutput:\n{Output}", output);
            throw new Exception($"Failed to generate TLS-auth key.");
        }

        if (!File.Exists(taKeyPath))
        {
            throw new FileNotFoundException("TLS-auth key was not created as expected.", taKeyPath);
        }

        logger.LogInformation("TLS-auth key successfully generated at: {Path}", taKeyPath);

        return taKeyPath;
    }
    
    private static readonly Regex BashPathRegex = new(@"^(/mnt/[a-z]|/[a-z])/", 
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static string ConvertToBashPath(string windowsPath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return windowsPath;
        
        if (BashPathRegex.IsMatch(windowsPath))
            return windowsPath;

        var driveLetter = char.ToLower(windowsPath[0]);
        var pathWithoutDrive = windowsPath.Substring(2).Replace('\\', '/');

        return IsRunningInWsl()
            ? $"/mnt/{driveLetter}{pathWithoutDrive}"
            : $"/{driveLetter}{pathWithoutDrive}";
    }
    
    private static bool IsRunningInWsl()
    {
        var os = RuntimeInformation.OSDescription.ToLower();
        return os.Contains("microsoft") || os.Contains("wsl");
    }
}