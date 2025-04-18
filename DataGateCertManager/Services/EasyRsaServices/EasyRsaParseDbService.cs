using System.Globalization;
using DataGateCertManager.Models;
using DataGateCertManager.Models.Enums;
using DataGateCertManager.Services.EasyRsaServices.Interfaces;

namespace DataGateCertManager.Services.EasyRsaServices;

public class EasyRsaParseDbService : IEasyRsaParseDbService
{
    private const string Filename = "index.txt"; // TODO: Load from config if needed
    private readonly ILogger<IEasyRsaParseDbService> _logger;

    public EasyRsaParseDbService(ILogger<IEasyRsaParseDbService> logger)
    {
        _logger = logger;
    }

    public async Task<List<CertificateCaInfo>> ParseCertificateInfoInIndexFileAsync(string pkiPath, 
        CancellationToken cancellationToken)
    {
        var indexFilePath = Path.Combine(pkiPath, Filename);
        var result = new List<CertificateCaInfo>();

        try
        {
            var lines = await File.ReadAllLinesAsync(indexFilePath, cancellationToken);
        
            foreach (var line in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();
            
                var parts = line.Split('\t');
                if (parts.Length >= 6)
                {
                    result.Add(new CertificateCaInfo
                    {
                        Status = ParseStatus(parts[0]),
                        ExpiryDate = ParseDate(parts[1]),
                        RevokeDate = !string.IsNullOrEmpty(parts[2]) ? ParseDate(parts[2]) : DateTime.MinValue,
                        SerialNumber = parts[3],
                        UnknownField = parts[4],
                        CommonName = parts[5].StartsWith("/CN=") ? parts[5][4..] : parts[5]
                    });
                }
            }

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to parse certificate in index file");
            throw;
        }
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
        // date format from index.txt: "YYMMDDHHMMSSZ", for example: "250128120000Z"
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