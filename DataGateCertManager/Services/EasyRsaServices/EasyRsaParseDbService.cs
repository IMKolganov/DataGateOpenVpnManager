using System.Globalization;
using DataGateCertManager.Models.Dto;
using DataGateCertManager.Models.Enums;
using DataGateCertManager.Services.EasyRsaServices.Interfaces;

namespace DataGateCertManager.Services.EasyRsaServices;

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

                    var certPath = ResolveCertificatePath(pkiPath, commonName, serial, isRevoked);

                    result.Add(new ServerCertificate
                    {
                        Status = status,
                        ExpiryDate = ParseDate(parts[1]),
                        RevokeDate = isRevoked ? ParseDate(parts[2]) : DateTime.MinValue,
                        SerialNumber = serial,
                        UnknownField = parts[4],
                        CommonName = commonName,
                        IsRevoked = isRevoked,
                        CertificatePath = certPath
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
    
    private string ResolveCertificatePath(string pkiPath, string commonName, string serial, bool isRevoked)
    {
        var issuedPath = Path.Combine(pkiPath, "issued", $"{commonName}.crt");
        var revokedPath = Path.Combine(pkiPath, "revoked", $"{commonName}.crt");
        var serialPath = Path.Combine(pkiPath, "certs_by_serial", $"{serial}.pem");

        string? resolvedPath = null;

        if (isRevoked)
        {
            if (File.Exists(revokedPath))
                resolvedPath = revokedPath;
            else if (File.Exists(serialPath))
                resolvedPath = serialPath;
        }
        else
        {
            if (File.Exists(issuedPath))
                resolvedPath = issuedPath;
        }

        if (resolvedPath == null)
        {
            logger.LogWarning("Certificate file not found for CommonName={CommonName}, Serial={Serial}", commonName, serial);
        }

        return resolvedPath ?? string.Empty;
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