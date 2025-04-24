using DataGateCertManager.Models.Dto;
using DataGateCertManager.Services.EasyRsaServices.Interfaces;
using DataGateCertManager.Services.Interfaces;

namespace DataGateCertManager.Services;

public class OvpnFileService(ILogger<IOvpnFileService> logger, IEasyRsaService easyRsaService)
    : IOvpnFileService
{
    public async Task<IssuedOvpnFile> AddOvpnFile(string easyRsaPath, string commonName, string ovpnFileDir, 
        string configTemplate, string serverIp, int serverPort,
        CancellationToken cancellationToken, string issuedTo = "openVpnClient")
    {
        logger.LogInformation("Step 1: Building client certificate...");
        var certResult = await easyRsaService.BuildCertificateAsync("easyRsaPath", 
            cancellationToken, commonName);

        var caCertPath = Path.Combine(easyRsaPath, "ca.crt");
        var caCertContent = await easyRsaService.ReadPemContentAsync(
                caCertPath ?? throw new InvalidOperationException("CaCertPath is null."),
                cancellationToken);
        var clientCertContent = await easyRsaService.ReadPemContentAsync(
            certResult.CertificatePath ?? throw new InvalidOperationException("CertificatePath is null."), 
            cancellationToken);
        var clientKeyContent =
            await File.ReadAllTextAsync(certResult.KeyPath ?? throw new InvalidOperationException("KeyPath is null."),
                cancellationToken);
        var taCertPath = Path.Combine(easyRsaPath, "ta.crt");
        var taCertContent =
            await File.ReadAllTextAsync(taCertPath ?? throw new InvalidOperationException("TaCertPath is null."),
                cancellationToken);
        
        logger.LogInformation("Step 3: Generating .ovpn file...");
        var ovpnContent = GenerateOvpnFile(configTemplate, serverIp, serverPort, caCertContent, 
            clientCertContent, clientKeyContent, taCertContent);

        logger.LogInformation("Step 4: Writing .ovpn file...");

        var targetDir = ovpnFileDir ?? throw new InvalidOperationException("OvpnFileDir is null.");
        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        var ovpnFilePath = Path.Combine(targetDir, $"{commonName}.ovpn");
        await File.WriteAllTextAsync(ovpnFilePath, ovpnContent, cancellationToken);

        logger.LogInformation("Client configuration file created: {Path}", ovpnFilePath);

        var fileInfo = new FileInfo(Path.GetFullPath(ovpnFilePath));
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("OVPN file was not created as expected.", fileInfo.FullName);
        }

        logger.LogInformation("Step 5: Saving metadata in database...");
        var issuedOvpnFile = new IssuedOvpnFile
        {
            CommonName = commonName,
            // CertId = certResult.CertId,//todo: check
            FileName = fileInfo.Name,
            FilePath = fileInfo.FullName,
            IssuedAt = DateTime.UtcNow,
            IssuedTo = issuedTo,
            CertFilePath = certResult.CertificatePath,
            IsRevoked = false
        };

        return new IssuedOvpnFile();
    }

    public async Task<IssuedOvpnFile?> RevokeOvpnFile(string easyRsaPath, string commonName, 
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
        var serverCertificate = await easyRsaService.RevokeCertificateAsync(
            easyRsaPath, commonName, cancellationToken);
        
        logger.LogInformation("RevokeCertificate result: {Message} " +
                               "for CertName: {CommonName}", serverCertificate.Message, commonName);
        string revokedFilePath = MoveRevokedOvpnFile(0, "ovpnFileDir", "revokedOvpnFilesDirPath",
            new IssuedOvpnFile());
        logger.LogInformation("Successfully moved revoked .ovpn file to: {RevokedFilePath}", revokedFilePath);

        logger.LogInformation("Updated database for revoked certificate: {CommonName}, " +
                               "External ID: {ExternalId}", commonName, 0);

        return new IssuedOvpnFile();
    }

    public async Task<OvpnFile> GetOvpnFile(string fileName, string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            throw new Exception($"File {filePath} does not exist");
        }

        try
        {
            var content = await File.ReadAllBytesAsync(filePath, cancellationToken);
            return new OvpnFile
            {
                FileName = fileName,
                Content = content
            };
        }
        catch (Exception ex)
        {
            throw new Exception($"Error reading OVPN file {filePath}: {ex.Message}", ex);
        }
    }
    
    private string MoveRevokedOvpnFile(int fileId, string ovpnFileDir, string revokedOvpnFilesDirPath, 
        IssuedOvpnFile issuedOvpnFile)
    {
        string ovpnFilePath = Path.Combine(ovpnFileDir, issuedOvpnFile.FileName);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var uniqueFileName = 
            $"{Path.GetFileNameWithoutExtension(issuedOvpnFile.FileName)}" +
            $"_{fileId}" +
            $"_{timestamp}" +
            $"{Path.GetExtension(issuedOvpnFile.FileName)}";

        string revokedFilePath = Path.Combine(revokedOvpnFilesDirPath, uniqueFileName);

        Directory.CreateDirectory(ovpnFileDir);
        Directory.CreateDirectory(revokedOvpnFilesDirPath);

        if (File.Exists(ovpnFilePath))
        {
            File.Move(ovpnFilePath, revokedFilePath);
            logger.LogInformation($"Moved .ovpn file to revoked folder: {revokedFilePath}");
        }
        else
        {
            logger.LogWarning($".ovpn file not found for moving: {ovpnFilePath}");
        }

        return revokedFilePath;
    }
    
    private static string GenerateOvpnFile(
        string configTemplate,
        string serverIp,
        int serverPort,
        string caCert,
        string clientCert,
        string clientKey,
        string tlsAuthKeyPath)
    {
        if (string.IsNullOrWhiteSpace(configTemplate))
            throw new ArgumentNullException(nameof(configTemplate));
        if (string.IsNullOrWhiteSpace(serverIp))
            throw new ArgumentNullException(nameof(serverIp));
        if (string.IsNullOrWhiteSpace(caCert))
            throw new ArgumentNullException(nameof(caCert));
        if (string.IsNullOrWhiteSpace(clientCert))
            throw new ArgumentNullException(nameof(clientCert));
        if (string.IsNullOrWhiteSpace(clientKey))
            throw new ArgumentNullException(nameof(clientKey));
        if (string.IsNullOrWhiteSpace(tlsAuthKeyPath))
            throw new ArgumentNullException(nameof(tlsAuthKeyPath));

        var tlsAuthKey = File.ReadAllText(tlsAuthKeyPath);

        return configTemplate
            .Replace("{{server_ip}}", serverIp)
            .Replace("{{server_port}}", serverPort.ToString())
            .Replace("{{ca_cert}}", caCert)
            .Replace("{{client_cert}}", clientCert)
            .Replace("{{client_key}}", clientKey)
            .Replace("{{tls_auth_key}}", tlsAuthKey);
    }
}