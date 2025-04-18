using DataGateCertManager.Models;
using DataGateCertManager.Models.Enums;
using DataGateCertManager.Services.EasyRsaServices.Interfaces;

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

    public async Task<CertificateBuildResult> BuildCertificate(string easyRsaPath, CancellationToken cancellationToken,
        string baseFileName = "client1")
    {
        var pkiPath = $"{easyRsaPath}/pki";
        _logger.LogInformation($"Starting certificate build for: {baseFileName}");
        var reqPath = Path.Combine(pkiPath, "reqs", $"{baseFileName}.req");
        var issuedPath = Path.Combine(pkiPath, "issued", $"{baseFileName}.crt");
        var keyPath = Path.Combine(pkiPath, "private", $"{baseFileName}.key");

        _logger.LogInformation($"Expected paths:\nREQ: {reqPath}\nCRT: {issuedPath}\nKEY: {keyPath}");

        if (File.Exists(reqPath))
        {
            _logger.LogWarning($"WARNING: Request file already exists before EasyRSA run: {reqPath}");
        }

        var command =
            $"cd {easyRsaPath} && ./easyrsa --batch build-client-full {baseFileName} nopass";
        _logger.LogInformation($"Executing EasyRSA command: {command}");

        var (output, error, exitCode) =
            await _easyRsaExecCommandService.RunCommand(command, cancellationToken);

        if (exitCode != 0)
        {
            if (File.Exists(reqPath))
            {
                _logger.LogWarning($"Request file exists after failed EasyRSA run: {reqPath}");
            }

            _logger.LogError($"EasyRSA output:\n{output}");
            _logger.LogError($"EasyRSA error:\n{error}");
            throw new Exception($"Error while building certificate: {error}. Output: {output}");
        }

        _logger.LogInformation($"Certificate generated successfully:\n{output}");



        var certificateInfoInIndexFile = await GetAllCertificateInfoInIndexFile(pkiPath,
            cancellationToken);
        certificateInfoInIndexFile = certificateInfoInIndexFile
            .Where(x => x.Status == CertificateStatus.Active && x.CommonName == baseFileName).ToList();


        if (certificateInfoInIndexFile.Count <= 0)
        {
            throw new Exception($"Error certificate is not found in CA {issuedPath}");
        }

        var certInfo = certificateInfoInIndexFile.First();
        var serialFromOpenSsl = await CheckCertInOpenssl(issuedPath, cancellationToken);

        if (!certInfo.SerialNumber.Contains(serialFromOpenSsl))
        {
            throw new Exception($"Certificate serial number {certInfo.SerialNumber} is invalid.");
        }

        var pemSerialPath = Path.Combine(pkiPath, "certs_by_serial",
            $"{certInfo.SerialNumber}.pem");

        _logger.LogInformation($"Certificate PEM path: {pemSerialPath}");

        return new CertificateBuildResult
        {
            CertificatePath = issuedPath,
            KeyPath = keyPath,
            RequestPath = reqPath,
            PemPath = pemSerialPath,
            CertId = certInfo.SerialNumber
        };
    }

    public async Task<string> ReadPemContent(string filePath, CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        return string.Join(Environment.NewLine, lines
            .SkipWhile(line => !line.StartsWith("-----BEGIN CERTIFICATE-----"))
            .TakeWhile(line => !line.StartsWith("-----END CERTIFICATE-----"))
            .Append("-----END CERTIFICATE-----"));
    }

    public async Task<CertificateRevokeResult> RevokeCertificate(string easyRsaPath, string commonName,
        CancellationToken cancellationToken)
    {
        var pkiPath = $"{easyRsaPath}/pki";
        var certificateRevokeResult = new CertificateRevokeResult
        {
            CertificatePath = Path.Combine(pkiPath, "issued", $"{commonName}.crt")
        };
        if (!File.Exists(certificateRevokeResult.CertificatePath))
        {
            _logger.LogInformation($"EasyRsa path: {easyRsaPath}");
            _logger.LogInformation($"PKI path: {pkiPath}");
            throw new Exception($"Certificate file not found: {certificateRevokeResult.CertificatePath}");
        }

        _logger.LogInformation($"Attempting to revoke certificate for: {commonName}");
        _logger.LogInformation($"EasyRsaPath: {easyRsaPath}");
        _logger.LogInformation($"PKI Path: {pkiPath}");
        _logger.LogInformation($"Certificate Path: {certificateRevokeResult.CertificatePath}");

        // Revoke the certificate
        var revokeResult = await _easyRsaExecCommandService.ExecuteEasyRsaCommand($"revoke {commonName}",
            easyRsaPath, cancellationToken, confirm: true);
        certificateRevokeResult.IsRevoked = revokeResult.IsSuccess;
        if (!certificateRevokeResult.IsRevoked)
        {
            switch (revokeResult.ExitCode)
            {
                case 0:
                    certificateRevokeResult.Message += $"Certificate revoked successfully: {commonName}";
                    _logger.LogInformation($"Certificate revoked successfully: {commonName}");
                    break;

                case 1:
                    if (revokeResult.Output.Contains("ERROR:Already revoked")
                        || revokeResult.Error.Contains("ERROR:Already revoked"))
                    {
                        certificateRevokeResult.Message += $"Certificate is already revoked: {commonName}";
                        _logger.LogWarning($"Certificate is already revoked: {commonName}");
                    }
                    else if (revokeResult.Output.Contains("ERROR: Certificate not found")
                             || revokeResult.Output.Contains("ERROR: Certificate not found"))
                    {
                        certificateRevokeResult.Message += $"Certificate not found: {commonName}";
                        _logger.LogWarning($"Certificate not found: {commonName}");
                    }
                    else
                    {
                        throw new Exception($"Failed to revoke certificate. Unknown error: {commonName}, " +
                                            $"ExitCode: {revokeResult.ExitCode}, Output: {revokeResult.Output}");
                    }

                    break;

                default:
                    throw new Exception($"Unexpected exit code ({revokeResult.ExitCode}) " +
                                        $"while revoking certificate: {commonName}");
            }
        }

        _logger.LogInformation("Revocation successful. Generating CRL...");
        if (await UpdateCrl(easyRsaPath, cancellationToken))
            _logger.LogInformation("CRL successfully updated and deployed.");

        _logger.LogInformation("Certificate successfully revoked, CRL updated and deployed.");
        return certificateRevokeResult;
    }

    public async Task<List<CertificateCaInfo>> GetAllCertificateInfoInIndexFile(string pkiPath,
        CancellationToken cancellationToken)
    {
        return await _easyRsaParseDbService.ParseCertificateInfoInIndexFileAsync(pkiPath, cancellationToken);
    }

    private void InstallEasyRsa(OpenVpnServerCertConfig openVpnServerCertConfig, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(openVpnServerCertConfig.PkiPath))
        {
            _logger.LogInformation("PKI directory does not exist. Initializing PKI...");
            _easyRsaExecCommandService.RunCommand(
                $"cd {openVpnServerCertConfig.EasyRsaPath} && EASYRSA_BATCH=1 ./easyrsa init-pki", cancellationToken);
            throw new Exception("PKI directory does not exist.");
        }
        else
        {
            _logger.LogInformation("PKI directory exists. Skipping initialization...");
        }
    }

    private async Task<string> CheckCertInOpenssl(string? certPath, CancellationToken cancellationToken)
    {
        var certPathCommand = $"openssl x509 -in {certPath} -serial -noout";
        var (certOutput, certError, certExitCode) =
            await _easyRsaExecCommandService.RunCommand(certPathCommand, cancellationToken);

        if (certExitCode != 0)
        {
            throw new Exception($"Error occurred while retrieving certificate serial: {certError}");
        }

        var serial = certOutput.Split('=')[1].Trim();
        _logger.LogInformation($"Certificate serial retrieved:\n{serial} Full response: \n{certOutput}");
        return serial;
    }


    private async Task<bool> UpdateCrl(string easyRsaPath, CancellationToken cancellationToken)
    {
        var crlPath = $"{easyRsaPath}/pki/crl.pem";
        var crlResult = await _easyRsaExecCommandService.ExecuteEasyRsaCommand(
            "gen-crl", easyRsaPath, cancellationToken);
        if (!crlResult.IsSuccess)
        {
            _logger.LogInformation($"Command Output: {crlResult.Output}");
            throw new Exception($"Failed to generate CRL: {crlResult.Error}");
        }

        if (!File.Exists(crlPath))
        {
            //todo: think about it and maybe use path from output
            throw new Exception($"Generated CRL not found at {crlPath}, Command Output: {crlResult.Output}");
        }

        _logger.LogInformation($"Generating CRL File: {crlPath}");

        return true;
    }
}