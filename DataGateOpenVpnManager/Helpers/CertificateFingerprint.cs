using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DataGateOpenVpnManager.Helpers;

public static class CertificateFingerprint
{
    /// <summary>Lower-case hex SHA-256 of the certificate DER; empty when the PEM cannot be parsed.</summary>
    public static string TrySha256Hex(string certificatePem)
    {
        try
        {
            using var certificate = X509Certificate2.CreateFromPem(certificatePem);
            return Convert.ToHexStringLower(SHA256.HashData(certificate.RawData));
        }
        catch (CryptographicException)
        {
            return string.Empty;
        }
    }
}
