using System.Globalization;
using DataGateOpenVpnManager.Services.EasyRsaServices.Interfaces;
using OpenVPNGateMonitor.SharedModels.DataGateOpenVpnManager.Cert.Responses;
using OpenVPNGateMonitor.SharedModels.Enums;

namespace DataGateOpenVpnManager.Services.EasyRsaServices;

public class EasyRsaParseDbService(ILogger<IEasyRsaParseDbService> logger) : IEasyRsaParseDbService
{
    private const string Filename = "index.txt"; // TODO: Load from config if needed

    public async Task<List<ServerCertificate>> ParseCertificateInfoInIndexFileAsync(string pkiPath, CancellationToken cancellationToken)
    {
        var indexFilePath = Path.Combine(pkiPath, Filename);

        if (!File.Exists(indexFilePath))
            throw new FileNotFoundException(indexFilePath);

        var result = new List<ServerCertificate>();

        try
        {
            var lines = await File.ReadAllLinesAsync(indexFilePath, cancellationToken);

            foreach (var line in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var parts = line.Split('\t');
                if (parts.Length >= 6)
                {
                    var status = ParseStatus(parts[0]);
                    var commonName = parts[5].StartsWith("/CN=") ? parts[5][4..] : parts[5];
                    var isRevoked = !string.IsNullOrEmpty(parts[2]);
                    var serial = parts[3];

                    var (certPath, keyPath) = ResolveCertificateAndKeyPaths(pkiPath, commonName, serial, isRevoked);

                    result.Add(new ServerCertificate
                    {
                        Status = status,
                        ExpiryDate = ParseDate(parts[1]),
                        RevokeDate = isRevoked ? ParseDate(parts[2]) : DateTime.MinValue,
                        SerialNumber = serial,
                        UnknownField = parts[4],
                        CommonName = commonName,
                        IsRevoked = isRevoked,
                        CertificatePath = certPath,
                        KeyPath = keyPath
                    });
                }
            }

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to parse certificate in index file");
            throw;
        }
    }
    
    private (string certPath, string keyPath) ResolveCertificateAndKeyPaths(string pkiPath, string commonName, 
        string serial, bool isRevoked)
    {
        var issuedCertPath = Path.Combine(pkiPath, "issued", $"{commonName}.crt");
        var revokedCertPath = Path.Combine(pkiPath, "revoked", $"{commonName}.crt");
        var certsBySerialPath = Path.Combine(pkiPath, "certs_by_serial", $"{serial}.pem");

        var keyPath = Path.Combine(pkiPath, "private", $"{commonName}.key");

        string? resolvedCertPath = null;

        if (isRevoked)
        {
            if (File.Exists(revokedCertPath))
                resolvedCertPath = revokedCertPath;
            else if (File.Exists(certsBySerialPath))
                resolvedCertPath = certsBySerialPath;
        }
        else
        {
            if (File.Exists(issuedCertPath))
                resolvedCertPath = issuedCertPath;
        }

        if (resolvedCertPath == null)
        {
            logger.LogWarning("Certificate file not found for CommonName={CommonName}, Serial={Serial}", commonName, serial);
        }

        if (!File.Exists(keyPath))
        {
            logger.LogWarning("Private key file not found for CommonName={CommonName}", commonName);
        }

        return (resolvedCertPath ?? string.Empty, File.Exists(keyPath) ? keyPath : string.Empty);
    }

    private static CertificateStatus ParseStatus(string status)
    {
        return status switch
        {
            "V" => CertificateStatus.Active,
            "R" => CertificateStatus.Revoked,
            "E" => CertificateStatus.Expired,
            _ => CertificateStatus.Unknown
        };
    }

    private static DateTime ParseDate(string dateString)
    {
        // date format from index.txt: "YYMMDDHHMMSSZ", for example, "250128120000Z"
        var raw = dateString.TrimEnd('Z');
        if (DateTime.TryParseExact(
                raw,
                "yyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var date))
        {
            return date;
        }

        throw new FormatException($"Invalid date format: {dateString}");
    }
}