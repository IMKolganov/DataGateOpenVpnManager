using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using DataGateOpenVpnManager.Services.EasyRsaServices.Interfaces;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Cert.Responses;
using DataGateMonitor.SharedModels.Enums;

namespace DataGateOpenVpnManager.Services.EasyRsaServices;

public class EasyRsaParseDbService(ILogger<IEasyRsaParseDbService> logger) : IEasyRsaParseDbService
{
    private const string Filename = "index.txt"; // TODO: Load from config if needed
    private const string ServerCertCommonName = "server";

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

                    var (certPath, keyPath) = ResolveCertificateAndKeyPaths(pkiPath, commonName, serial, isRevoked, status);

                    result.Add(new ServerCertificate
                    {
                        Status = status,
                        ExpiryDate = ParseDate(parts[1]),
                        RevokeDate = isRevoked ? ParseDate(parts[2]) : null,
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

    private (string certPath, string keyPath) ResolveCertificateAndKeyPaths(
        string pkiPath,
        string commonName,
        string serial,
        bool isRevoked,
        CertificateStatus status)
    {
        var issuedCertPath = Path.Combine(pkiPath, "issued", $"{commonName}.crt");
        var revokedCertPath = Path.Combine(pkiPath, "revoked", $"{commonName}.crt");
        var certsBySerialPath = Path.Combine(pkiPath, "certs_by_serial", $"{serial}.pem");
        var revokedCertsBySerialCrtPath = Path.Combine(pkiPath, "revoked", "certs_by_serial", $"{serial}.crt");
        var caCertPath = Path.Combine(pkiPath, "ca.crt");
        var serverCertPath = Path.Combine(pkiPath, "issued", $"{ServerCertCommonName}.crt");

        string? resolvedCertPath = null;

        if (isRevoked)
        {
            resolvedCertPath = ResolveRevokedCertificatePath(
                certsBySerialPath,
                revokedCertsBySerialCrtPath,
                revokedCertPath,
                serial);
        }
        else if (File.Exists(issuedCertPath))
        {
            resolvedCertPath = issuedCertPath;
        }
        else if (File.Exists(certsBySerialPath))
        {
            resolvedCertPath = certsBySerialPath;
        }
        else if (IsCaIndexEntry(commonName, serial, caCertPath))
        {
            resolvedCertPath = caCertPath;
        }
        else if (string.Equals(commonName, ServerCertCommonName, StringComparison.OrdinalIgnoreCase)
                 && File.Exists(serverCertPath))
        {
            resolvedCertPath = serverCertPath;
        }

        var resolvedKeyPath = isRevoked
            ? ResolvePrivateKeyPathIfPresent(pkiPath, commonName, serial, resolvedCertPath, caCertPath, serverCertPath)
            : ResolvePrivateKeyPath(pkiPath, commonName, serial, resolvedCertPath, caCertPath, serverCertPath);

        if (string.IsNullOrEmpty(resolvedCertPath) && ShouldWarnMissingCertificate(isRevoked, status))
        {
            logger.LogWarning("Certificate file not found for CommonName={CommonName}, Serial={Serial}", commonName, serial);
        }

        if (string.IsNullOrEmpty(resolvedKeyPath) && ShouldWarnMissingPrivateKey(isRevoked, status))
        {
            logger.LogWarning("Private key file not found for CommonName={CommonName}", commonName);
        }

        return (resolvedCertPath ?? string.Empty, resolvedKeyPath ?? string.Empty);
    }

    /// <summary>
    /// Revoked certs are keyed by serial in Easy-RSA; <c>revoked/{cn}.crt</c> only reflects the latest revoke for that CN.
    /// </summary>
    private static string? ResolveRevokedCertificatePath(
        string certsBySerialPath,
        string revokedCertsBySerialCrtPath,
        string revokedCertPath,
        string serial)
    {
        if (File.Exists(certsBySerialPath))
            return certsBySerialPath;

        if (File.Exists(revokedCertsBySerialCrtPath))
            return revokedCertsBySerialCrtPath;

        if (File.Exists(revokedCertPath) && SerialMatchesCertificateFile(revokedCertPath, serial))
            return revokedCertPath;

        return null;
    }

    private static bool ShouldWarnMissingCertificate(bool isRevoked, CertificateStatus status) =>
        !isRevoked && status == CertificateStatus.Active;

    private static bool ShouldWarnMissingPrivateKey(bool isRevoked, CertificateStatus status) =>
        !isRevoked && status == CertificateStatus.Active;

    private static string? ResolvePrivateKeyPathIfPresent(
        string pkiPath,
        string commonName,
        string serial,
        string? resolvedCertPath,
        string caCertPath,
        string serverCertPath) =>
        ResolvePrivateKeyPath(pkiPath, commonName, serial, resolvedCertPath, caCertPath, serverCertPath);

    /// <summary>
    /// Easy-RSA stores the CA in <c>pki/ca.crt</c> while <c>index.txt</c> uses the CA subject CN
    /// (from <c>EASYRSA_REQ_CN</c>, often "OpenVPN-Server"), not a file under <c>issued/</c>.
    /// </summary>
    private static bool IsCaIndexEntry(string commonName, string serial, string caCertPath)
    {
        if (!File.Exists(caCertPath))
            return false;

        if (SerialMatchesCertificateFile(caCertPath, serial))
            return true;

        return CaSubjectMatchesCommonName(caCertPath, commonName);
    }

    private static X509Certificate2? LoadCertificatePem(string certPath)
    {
        try
        {
            return X509Certificate2.CreateFromPem(File.ReadAllText(certPath));
        }
        catch
        {
            return null;
        }
    }

    private static bool CaSubjectMatchesCommonName(string caCertPath, string commonName)
    {
        var cert = LoadCertificatePem(caCertPath);
        if (cert == null)
            return false;

        var cn = cert.GetNameInfo(X509NameType.SimpleName, false);
        return string.Equals(cn, commonName, StringComparison.Ordinal);
    }

    private static string? ResolvePrivateKeyPath(
        string pkiPath,
        string commonName,
        string serial,
        string? resolvedCertPath,
        string caCertPath,
        string serverCertPath)
    {
        var defaultKeyPath = Path.Combine(pkiPath, "private", $"{commonName}.key");
        if (File.Exists(defaultKeyPath))
            return defaultKeyPath;

        if (!string.IsNullOrEmpty(resolvedCertPath))
        {
            if (PathsEqual(resolvedCertPath, caCertPath))
            {
                var caKeyPath = Path.Combine(pkiPath, "private", "ca.key");
                if (File.Exists(caKeyPath))
                    return caKeyPath;
            }

            if (PathsEqual(resolvedCertPath, serverCertPath))
            {
                var serverKeyPath = Path.Combine(pkiPath, "private", $"{ServerCertCommonName}.key");
                if (File.Exists(serverKeyPath))
                    return serverKeyPath;
            }
        }

        if (IsCaIndexEntry(commonName, serial, caCertPath))
        {
            var caKeyPath = Path.Combine(pkiPath, "private", "ca.key");
            if (File.Exists(caKeyPath))
                return caKeyPath;
        }

        if (string.Equals(commonName, ServerCertCommonName, StringComparison.OrdinalIgnoreCase))
        {
            var serverKeyPath = Path.Combine(pkiPath, "private", $"{ServerCertCommonName}.key");
            if (File.Exists(serverKeyPath))
                return serverKeyPath;
        }

        return null;
    }

    private static bool SerialMatchesCertificateFile(string certPath, string indexSerial)
    {
        var cert = LoadCertificatePem(certPath);
        if (cert == null)
            return false;

        if (SerialEquals(cert.SerialNumber, indexSerial))
            return true;

        var bytes = cert.GetSerialNumber();
        if (bytes.Length == 0)
            return false;

        Array.Reverse(bytes);
        return SerialEquals(Convert.ToHexString(bytes), indexSerial);
    }

    private static bool SerialEquals(string certSerial, string indexSerial) =>
        string.Equals(NormalizeSerial(certSerial), NormalizeSerial(indexSerial), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSerial(string serial) =>
        serial.Replace(":", string.Empty, StringComparison.Ordinal).Trim().TrimStart('0');

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

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

    private static DateTimeOffset ParseDate(string dateString)
    {
        // date format from index.txt: "YYMMDDHHMMSSZ", for example, "250128120000Z"
        var raw = dateString.TrimEnd('Z');
        if (DateTime.TryParseExact(
                raw,
                "yyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var date))
        {
            return new DateTimeOffset(date, TimeSpan.Zero);
        }

        throw new FormatException($"Invalid date format: {dateString}");
    }
}
