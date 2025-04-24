using DataGateCertManager.Models.Dto;
using DataGateCertManager.Services.EasyRsaServices.Interfaces;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace DataGateCertManager.Services.EasyRsaServices;

public class EasyRsaService : IEasyRsaService
{
    private readonly ILogger<IEasyRsaService> _logger;
    private readonly IEasyRsaParseDbService _easyRsaParseDbService;
    private readonly IEasyRsaExecCommandService _easyRsaExecCommandService;

    public EasyRsaService(ILogger<IEasyRsaService> logger, IEasyRsaParseDbService easyRsaParseDbService,
        IEasyRsaExecCommandService easyRsaExecCommandService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _easyRsaParseDbService = easyRsaParseDbService;
        _easyRsaExecCommandService = easyRsaExecCommandService;
    }

    #region easyrsa build-client-full

// # ==============================================================================
// # EasyRSA build-client-full command variations
// # ==============================================================================
//
// | Command Example                                                                          | Description                                             |
// |------------------------------------------------------------------------------------------|---------------------------------------------------------|
// | `./easyrsa build-client-full client1`                                                    | Creates a client certificate with a password prompt     |
// | `./easyrsa build-client-full client1 nopass`                                             | Creates a client certificate without a password         |
// | `EASYRSA_BATCH=1 ./easyrsa build-client-full client1 nopass`                             | Skips confirmation prompts during execution             |
// | `EASYRSA_CERT_EXPIRE=3650 ./easyrsa build-client-full client1 nopass`                    | Sets the certificate expiration to 10 years (3650 days) |
// | `EASYRSA_KEY_SIZE=4096 ./easyrsa build-client-full client1 nopass`                       | Generates a 4096-bit key instead of the default 2048-bit|
// | `EASYRSA_DIGEST="sha512" ./easyrsa build-client-full client1 nopass`                     | Uses SHA-512 hashing instead of the default SHA-256     |
// | `EASYRSA_ALGO="ec" EASYRSA_CURVE="secp384r1" ./easyrsa build-client-full client1 nopass` | Uses ECDSA instead of RSA with secp384r1 curve          |
// | `EASYRSA_PASSIN="mypassword" ./easyrsa build-client-full client1`                        | Automatically provides the password instead of prompting|
// | `EASYRSA_PASSIN=file:/path/to/password.txt ./easyrsa build-client-full client1`          | Reads the password from a file                          |
// | `EASYRSA_SUBDIR=clients ./easyrsa build-client-full client1 nopass`                      | Stores the certificate and key in a custom subdirectory |
// | `EASYRSA_REQ_CN="My Custom CN" ./easyrsa build-client-full client1 nopass`               | Sets a custom Common Name (CN) in the certificate       |
// | `EASYRSA_REQ_SAN="DNS:client1.example.com" ./easyrsa build-client-full client1 nopass`   | Adds a Subject Alternative Name (SAN) to the certificate|
// | `EASYRSA_REQ_EMAIL="client1@example.com" ./easyrsa build-client-full client1 nopass`     | Specifies an email address in the certificate request   |
// | `EASYRSA_REQ_OU="MyOrgUnit" ./easyrsa build-client-full client1 nopass`                  | Adds an Organizational Unit to the certificate          |
//
// # ==============================================================================

    #endregion

    public async Task<ServerCertificate> BuildCertificateAsync(string easyRsaPath, CancellationToken cancellationToken,
        string commonName = "client1", int certExpireDays = 365)
    {
        easyRsaPath = Path.GetFullPath(easyRsaPath);
        var unixStylePath = ConvertToBashPath(easyRsaPath);

        var pkiPath = Path.Combine(easyRsaPath, "pki");
        var reqPath = Path.Combine(pkiPath, "reqs", $"{commonName}.req");

        _logger.LogInformation("Starting certificate build for: {CommonName}", commonName);

        string command;
        
        string env = $"EASYRSA_BATCH=1 EASYRSA_CERT_EXPIRE={certExpireDays}";

        if (File.Exists(reqPath))
        {
            _logger.LogWarning(
                "Request file already exists: {ReqPath}. Using existing request to sign new certificate.", reqPath);
            command = $"cd \"{unixStylePath}\" && {env} ./easyrsa sign client {commonName}";
        }
        else
        {
            command = $"cd \"{unixStylePath}\" && {env} ./easyrsa build-client-full {commonName} nopass";
        }
        
        _logger.LogInformation("Executing EasyRSA command: {Command}", command);

        var (output, error, exitCode) = await _easyRsaExecCommandService.RunCommandAsync(command, cancellationToken);

        if (exitCode != 0)
        {
            _logger.LogError("EasyRSA output:\n{Output}", output);
            _logger.LogError("EasyRSA error:\n{Error}", error);
            throw new Exception($"Error while building certificate: {error}. Output: {output}");
        }

        _logger.LogInformation("Certificate generated successfully:\n{Output}", output);

        if (await UpdateCrlAsync(easyRsaPath, cancellationToken))
        {
            _logger.LogInformation("CRL updated successfully.");
        }
        var certPath = ExtractCertificatePathFromOutput(output);
        var serialFromOpenSsl = await CheckCertInOpensslAsync(certPath, cancellationToken);
        var serverCertificate = await MatchingCertsAsync(easyRsaPath, serialFromOpenSsl, commonName, cancellationToken);
        

        if (!serverCertificate.SerialNumber.Contains(serialFromOpenSsl))
        {
            throw new Exception(
                $"Certificate serial number mismatch. Expected in OpenSSL: {serialFromOpenSsl}, " +
                $"Found in Index: {serverCertificate.SerialNumber}");
        }

        var pemSerialPath = Path.Combine(pkiPath, "certs_by_serial", $"{serverCertificate.SerialNumber}.pem");
        _logger.LogInformation("Certificate PEM path: {PemSerialPath}", pemSerialPath);

        return serverCertificate;
    }

    public async Task<string> ReadPemContentAsync(string filePath, CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        return string.Join(Environment.NewLine, lines
            .SkipWhile(line => !line.StartsWith("-----BEGIN CERTIFICATE-----"))
            .TakeWhile(line => !line.StartsWith("-----END CERTIFICATE-----"))
            .Append("-----END CERTIFICATE-----"));
    }

    public async Task<ServerCertificate> RevokeCertificateAsync(string easyRsaPath, string commonName,
        CancellationToken cancellationToken)
    {
        var serverCertificate = new ServerCertificate();
        var serialNumber = string.Empty;
        easyRsaPath = Path.GetFullPath(easyRsaPath);
        var unixStylePath = ConvertToBashPath(easyRsaPath);
        var pkiPath = Path.Combine(easyRsaPath, "pki");
        var issuedPath = Path.Combine(pkiPath, "issued", $"{commonName}.crt");

        if (!File.Exists(issuedPath))
        {
            _logger.LogWarning("Certificate file not found: {Path}", issuedPath);
            throw new FileNotFoundException($"Certificate file not found: {issuedPath}");
        }

        _logger.LogInformation("Revoking certificate for: {CommonName}", commonName);

        var revokeCommand = $"cd \"{unixStylePath}\" && EASYRSA_BATCH=1 ./easyrsa revoke {commonName}";
        var (output, error, exitCode) =
            await _easyRsaExecCommandService.RunCommandAsync(revokeCommand, cancellationToken);
        
        if (exitCode == 0)
        {
            serialNumber = ExtractSerialFromRevocationOutput(error) ?? string.Empty;
            _logger.LogInformation("Certificate revoked successfully: " +
                                   "{CommonName}, SerialNumber: {SerialNumber}, " +
                                   "Output: {Output} Error: {Error}", commonName, serialNumber, output, error);
        }
        else
        {
            if (output.Contains("ERROR:Already revoked", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("ERROR:Already revoked", StringComparison.OrdinalIgnoreCase))
            {
                serverCertificate.Message = $"Certificate is already revoked: {commonName}";
                _logger.LogWarning("Certificate is already revoked: {CommonName}", commonName);
            }
            else if (output.Contains("ERROR: Certificate not found", StringComparison.OrdinalIgnoreCase) ||
                     error.Contains("ERROR: Certificate not found", StringComparison.OrdinalIgnoreCase))
            {
                serverCertificate.Message = $"Certificate not found: {commonName}";
                _logger.LogWarning("Certificate not found: {CommonName}", commonName);
            }
            else
            {
                throw new Exception($"Unknown error during revocation. ExitCode: {exitCode}, Output: {output}");
            }
        }

        _logger.LogInformation("Generating updated CRL...");
        if (await UpdateCrlAsync(easyRsaPath, cancellationToken))
        {
            _logger.LogInformation("CRL updated successfully.");
        }

        serverCertificate = await MatchingCertsAsync(easyRsaPath, serialNumber, commonName, cancellationToken);
        return serverCertificate;
    }

    public async Task<List<ServerCertificate>> GetAllCertificateInfoInIndexFileAsync(string easyRsaPath,
        CancellationToken cancellationToken)
    {
        var fullEasyRsaPath = Path.GetFullPath(easyRsaPath);
        var fullEasyRsaPkiPath = Path.Combine(fullEasyRsaPath, "pki");
        
        if (!Directory.Exists(fullEasyRsaPkiPath))
        {
            await InstallEasyRsaAsync(fullEasyRsaPath, cancellationToken);
        }
        
        return await _easyRsaParseDbService.ParseCertificateInfoInIndexFileAsync(fullEasyRsaPkiPath, cancellationToken);
    }

    private async Task InstallEasyRsaAsync(string easyRsaPath, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing EasyRSA...");

        var unixStylePath = ConvertToBashPath(easyRsaPath);
        var scriptPath = Path.Combine(easyRsaPath, "easyrsa");

        if (!File.Exists(scriptPath))
            throw new FileNotFoundException($"EasyRSA script not found at: {scriptPath}");

        try
        {
            var chmodCommand = $"chmod +x \"{unixStylePath}/easyrsa\"";
            await _easyRsaExecCommandService.RunCommandAsync(chmodCommand, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"chmod failed: {ex.Message}");
        }

        var caPath = Path.Combine(easyRsaPath, "pki", "ca.crt");
        var isPkiDirExists = Directory.Exists(Path.Combine(easyRsaPath, "pki"));

        if (!isPkiDirExists)
        {
            var initCommand = $"cd \"{unixStylePath}\" && EASYRSA_BATCH=1 ./easyrsa init-pki";
            _logger.LogInformation("Running EasyRSA init-pki...");

            var (initOut, initErr, initExit) =
                await _easyRsaExecCommandService.RunCommandAsync(initCommand, cancellationToken);
            if (initExit != 0)
            {
                _logger.LogError("init-pki failed. Output: {Output}, Error: {Error}", initOut, initErr);
                throw new Exception($"Failed to initialize PKI. Error: {initErr}");
            }

            _logger.LogInformation("PKI initialized successfully");
        }

        if (!File.Exists(caPath))
        {
            var buildCaCommand = $"cd \"{unixStylePath}\" && EASYRSA_BATCH=1 ./easyrsa build-ca nopass";
            _logger.LogInformation("No CA certificate found. Running build-ca...");

            var (caOut, caErr, caExit) =
                await _easyRsaExecCommandService.RunCommandAsync(buildCaCommand, cancellationToken);
            if (caExit != 0)
            {
                _logger.LogError("build-ca failed. Output: {Output}, Error: {Error}", caOut, caErr);
                throw new Exception($"Failed to build CA certificate. Error: {caErr}");
            }

            _logger.LogInformation("CA certificate created successfully");
        }
        else
        {
            _logger.LogInformation("CA certificate already exists. Skipping build-ca.");
        }
    }

    private async Task<string> CheckCertInOpensslAsync(string? certPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(certPath))
            throw new ArgumentException("Certificate path is null or empty");

        var opensslPath = ConvertToBashPath(certPath);
        var certPathCommand = $"openssl x509 -in \"{opensslPath}\" -serial -noout";

        var (certOutput, certError, certExitCode) =
            await _easyRsaExecCommandService.RunCommandAsync(certPathCommand, cancellationToken);

        if (certExitCode != 0)
        {
            throw new Exception($"Error occurred while retrieving certificate serial: {certError}");
        }

        var serial = certOutput.Split('=')[1].Trim();
        _logger.LogInformation("Certificate serial retrieved:\n{Serial}\nFull OpenSSL response:\n{Output}", 
            serial, certOutput);
        return serial;
    }

    private async Task<bool> UpdateCrlAsync(string easyRsaPath, CancellationToken cancellationToken)
    {
        easyRsaPath = Path.GetFullPath(easyRsaPath);
        var unixStylePath = ConvertToBashPath(easyRsaPath);
        var crlPath = Path.Combine(easyRsaPath, "pki", "crl.pem");

        var command = $"cd \"{unixStylePath}\" && EASYRSA_BATCH=1 ./easyrsa gen-crl";
        _logger.LogInformation("Executing EasyRSA CRL generation command: {Command}", command);

        var (output, error, exitCode) = await _easyRsaExecCommandService.RunCommandAsync(command, 
            cancellationToken);

        if (exitCode != 0)
        {
            _logger.LogError("CRL generation failed. Output: {Output}, Error: {Error}", output, error);
            throw new Exception($"Failed to generate CRL. Error: {error}");
        }

        if (!File.Exists(crlPath))
        {
            throw new FileNotFoundException($"Generated CRL not found at {crlPath}. Command output: {output}");
        }

        _logger.LogInformation("CRL successfully generated at: {CrlPath}", crlPath);
        return true;
    }

    private async Task<ServerCertificate> MatchingCertsAsync(string easyRsaPath, string serialNumber, string commonName,
        CancellationToken cancellationToken)
    {
        var certificateInfoInIndexFile = await GetAllCertificateInfoInIndexFileAsync(easyRsaPath, 
            cancellationToken);
        var matchingCerts = certificateInfoInIndexFile
            .Where(x => x.SerialNumber == serialNumber& x.CommonName == commonName)
            .ToList();

        if (!matchingCerts.Any())
        {
            throw new Exception($"Certificate not found in index.txt after generation.");
        }

        return matchingCerts.First();
    }
    
    private static bool IsRunningInWsl()
    {
        var os = RuntimeInformation.OSDescription.ToLower();
        return os.Contains("microsoft") || os.Contains("wsl");
    }

    private static readonly Regex BashPathRegex = new(@"^(/mnt/[a-z]|/[a-z])/", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
    
    private static string? ExtractSerialFromRevocationOutput(string stderr)
    {
        var match = Regex.Match(stderr, @"Revoking Certificate (\w{16,})", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }
    private static string? ExtractCertificatePathFromOutput(string output)
    {
        const string marker = "Certificate created at:";
    
        using var reader = new StringReader(output);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.StartsWith(marker))
            {
                var nextLine = reader.ReadLine();
                if (nextLine != null && nextLine.Trim().StartsWith("*"))
                {
                    return nextLine.Trim().TrimStart('*', ' ');
                }
            }
        }

        return null;
    }
}