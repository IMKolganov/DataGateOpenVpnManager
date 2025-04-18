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
// # | Command Example                                      | Description                                      |
// # |------------------------------------------------------|--------------------------------------------------|
// # | ./easyrsa build-client-full client1                 | Creates a client certificate with a password prompt |
// # | ./easyrsa build-client-full client1 nopass          | Creates a client certificate without a password |
// # | EASYRSA_BATCH=1 ./easyrsa build-client-full client1 nopass  | Skips confirmation prompts during execution |
// # | EASYRSA_CERT_EXPIRE=3650 ./easyrsa build-client-full client1 nopass | Sets the certificate expiration to 10 years (3650 days) |
// # | EASYRSA_KEY_SIZE=4096 ./easyrsa build-client-full client1 nopass | Generates a 4096-bit key instead of the default 2048-bit |
// # | EASYRSA_DIGEST="sha512" ./easyrsa build-client-full client1 nopass | Uses SHA-512 hashing instead of the default SHA-256 |
// # | EASYRSA_ALGO="ec" EASYRSA_CURVE="secp384r1" ./easyrsa build-client-full client1 nopass | Uses ECDSA instead of RSA with secp384r1 curve |
// # | EASYRSA_PASSIN="mypassword" ./easyrsa build-client-full client1 | Automatically provides the password instead of prompting |
// # | EASYRSA_PASSIN=file:/path/to/password.txt ./easyrsa build-client-full client1 | Reads the password from a file |
// # ==============================================================================

    #endregion

    public CertificateBuildResult BuildCertificate(OpenVpnServerCertConfig openVpnServerCertConfig,
        string baseFileName = "client1")
    {
        _logger.LogInformation($"Starting certificate build for: {baseFileName}");
        var reqPath = Path.Combine(openVpnServerCertConfig.PkiPath, "reqs", $"{baseFileName}.req");
        var issuedPath = Path.Combine(openVpnServerCertConfig.PkiPath, "issued", $"{baseFileName}.crt");
        var keyPath = Path.Combine(openVpnServerCertConfig.PkiPath, "private", $"{baseFileName}.key");

        _logger.LogInformation($"Expected paths:\nREQ: {reqPath}\nCRT: {issuedPath}\nKEY: {keyPath}");

        if (File.Exists(reqPath))
        {
            _logger.LogWarning($"WARNING: Request file already exists before EasyRSA run: {reqPath}");
        }

        var command =
            $"cd {openVpnServerCertConfig.EasyRsaPath} && ./easyrsa --batch build-client-full {baseFileName} nopass";
        _logger.LogInformation($"Executing EasyRSA command: {command}");

        var (output, error, exitCode) = _easyRsaExecCommandService.RunCommand(command);

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

        var certificateInfoInIndexFile = GetAllCertificateInfoInIndexFile(openVpnServerCertConfig.PkiPath)
            .Where(x => x.Status == CertificateStatus.Active && x.CommonName == baseFileName).ToList();

        if (certificateInfoInIndexFile.Count <= 0)
        {
            throw new Exception($"Error certificate is not found in CA {issuedPath}");
        }

        var certInfo = certificateInfoInIndexFile.First();
        var serialFromOpenSsl = CheckCertInOpenssl(issuedPath);

        if (!certInfo.SerialNumber.Contains(serialFromOpenSsl))
        {
            throw new Exception($"Certificate serial number {certInfo.SerialNumber} is invalid.");
        }

        var pemSerialPath = Path.Combine(openVpnServerCertConfig.PkiPath, "certs_by_serial",
            $"{certInfo.SerialNumber}.pem");

        _logger.LogInformation($"Certificate PEM path: {pemSerialPath}");

        return new CertificateBuildResult
        {
            VpnServerId = openVpnServerCertConfig.VpnServerId,
            CertificatePath = issuedPath,
            KeyPath = keyPath,
            RequestPath = reqPath,
            PemPath = pemSerialPath,
            CertId = certInfo.SerialNumber
        };
    }

    public string ReadPemContent(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        return string.Join(Environment.NewLine, lines
            .SkipWhile(line => !line.StartsWith("-----BEGIN CERTIFICATE-----"))
            .TakeWhile(line => !line.StartsWith("-----END CERTIFICATE-----"))
            .Append("-----END CERTIFICATE-----"));
    }

    public CertificateRevokeResult RevokeCertificate(OpenVpnServerCertConfig openVpnServerCertConfig, string commonName)
    {
        var certificateRevokeResult = new CertificateRevokeResult
        {
            CertificatePath = Path.Combine(openVpnServerCertConfig.PkiPath, "issued", $"{commonName}.crt")
        };
        if (!File.Exists(certificateRevokeResult.CertificatePath))
        {
            _logger.LogInformation($"EasyRsa path: {openVpnServerCertConfig.EasyRsaPath}");
            _logger.LogInformation($"PKI path: {openVpnServerCertConfig.PkiPath}");
            throw new Exception($"Certificate file not found: {certificateRevokeResult.CertificatePath}");
        }

        _logger.LogInformation($"Attempting to revoke certificate for: {commonName}");
        _logger.LogInformation($"EasyRsaPath: {openVpnServerCertConfig.EasyRsaPath}");
        _logger.LogInformation($"PKI Path: {openVpnServerCertConfig.PkiPath}");
        _logger.LogInformation($"Certificate Path: {certificateRevokeResult.CertificatePath}");

        // Revoke the certificate
        var revokeResult = _easyRsaExecCommandService.ExecuteEasyRsaCommand($"revoke {commonName}", 
            openVpnServerCertConfig.EasyRsaPath, confirm: true);
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
        if (UpdateCrl(openVpnServerCertConfig))
            _logger.LogInformation("CRL successfully updated and deployed.");

        _logger.LogInformation("Certificate successfully revoked, CRL updated and deployed.");
        return certificateRevokeResult;
    }
    
    public List<CertificateCaInfo> GetAllCertificateInfoInIndexFile(string pkiPath)
    {
        return _easyRsaParseDbService.ParseCertificateInfoInIndexFile(pkiPath);
    }
    
    public bool CheckHealthFileSystem(OpenVpnServerCertConfig openVpnServerCertConfig)
    {
        InstallEasyRsa(openVpnServerCertConfig);
        
        Directory.CreateDirectory(openVpnServerCertConfig.OvpnFileDir);
        Directory.CreateDirectory(openVpnServerCertConfig.RevokedOvpnFilesDirPath);
        
        if (!Directory.Exists(openVpnServerCertConfig.OvpnFileDir))
        {
            throw new FileNotFoundException("The output directory could not be found.");
        }
        if (!Directory.Exists(openVpnServerCertConfig.RevokedOvpnFilesDirPath))
        {
            throw new FileNotFoundException("Revoked folder not found");
        }
        
        var indexFilePath = Path.Combine(openVpnServerCertConfig.PkiPath, "index.txt");
        if (!File.Exists(indexFilePath))
        {
            throw new FileNotFoundException($"Index file not found at path: {indexFilePath}");
        }

        if (string.IsNullOrEmpty(openVpnServerCertConfig.CaCertPath))
            throw new ArgumentNullException(nameof(openVpnServerCertConfig.CaCertPath));
        if (string.IsNullOrEmpty(openVpnServerCertConfig.TlsAuthKey))
            throw new ArgumentNullException(nameof(openVpnServerCertConfig.TlsAuthKey));

        return true;
    }
    
    private void InstallEasyRsa(OpenVpnServerCertConfig openVpnServerCertConfig)
    {
        if (!Directory.Exists(openVpnServerCertConfig.PkiPath))
        {
            _logger.LogInformation("PKI directory does not exist. Initializing PKI...");
            _easyRsaExecCommandService.RunCommand($"cd {openVpnServerCertConfig.EasyRsaPath} && EASYRSA_BATCH=1 ./easyrsa init-pki");
            throw new Exception("PKI directory does not exist.");
        }
        else
        {
            _logger.LogInformation("PKI directory exists. Skipping initialization...");
        }
    }
    
    private string CheckCertInOpenssl(string certPath)
    {
        var certPathCommand = $"openssl x509 -in {certPath} -serial -noout";
        var (certOutput, certError, certExitCode) = _easyRsaExecCommandService.RunCommand(certPathCommand);

        if (certExitCode != 0)
        {
            throw new Exception($"Error occurred while retrieving certificate serial: {certError}");
        }

        var serial = certOutput.Split('=')[1].Trim();
        _logger.LogInformation($"Certificate serial retrieved:\n{serial} Full response: \n{certOutput}");
        return serial;
    }

    
    private bool UpdateCrl(OpenVpnServerCertConfig openVpnServerCertConfig)
    {
        var crlResult = _easyRsaExecCommandService.ExecuteEasyRsaCommand("gen-crl", 
            openVpnServerCertConfig.EasyRsaPath);
        if (!crlResult.IsSuccess)
        {
            _logger.LogInformation($"Command Output: {crlResult.Output}");
            throw new Exception($"Failed to generate CRL: {crlResult.Error}");
        }
        
        if (!File.Exists(openVpnServerCertConfig.CrlPkiPath))
        {
            throw new Exception($"Generated CRL not found at {openVpnServerCertConfig.CrlPkiPath}," +
                                $" Command Output: {crlResult.Output}");
        }
        
        try
        {
            // Copy the CRL to the OpenVPN directory
            string copyCommand = $"cp {openVpnServerCertConfig.CrlPkiPath} {openVpnServerCertConfig.CrlOpenvpnPath}";
            var copyResult = _easyRsaExecCommandService.RunCommand(copyCommand);

            if (copyResult.ExitCode != 0)
            {
                _logger.LogInformation($"Command Output: {copyResult.Output}");
                throw new Exception($"Failed to copy CRL file: {copyResult.Error}");
            }

            _logger.LogInformation($"copyResult - {copyResult}");
            _logger.LogInformation($"CRL copied to {openVpnServerCertConfig.CrlOpenvpnPath}");
        }
        catch (Exception ex)
        {
            throw new Exception($"Error during CRL update: {ex.Message}");
        }

        return true;
    }
}